"""Linux no-follow, root-relative bounded snapshot. No fallback to path-based reads."""
import ctypes
import errno
import os
import platform
import re
import stat
from .model import Location, Reject

SEGMENT = re.compile(r'[A-Za-z0-9][A-Za-z0-9._-]*\Z')


def inventory_path(path):
    return isinstance(path, str) and bool(path) and all(SEGMENT.fullmatch(x) for x in path.split('/')) and all(x not in ('.', '..') for x in path.split('/'))


def identity(s):
    return (s.st_dev, s.st_ino, s.st_mode, s.st_nlink, s.st_size, s.st_mtime_ns, s.st_ctime_ns)


class OpenHow(ctypes.Structure):
    _fields_ = [('flags', ctypes.c_uint64), ('mode', ctypes.c_uint64), ('resolve', ctypes.c_uint64)]


class Snapshot:
    def __init__(self, root, limits, hook=None):
        self.root, self.limits, self.hook = os.fspath(root), limits, hook
        self.fd = None
        self.locations = {}

    def __enter__(self):
        if platform.system() != 'Linux' or platform.machine() not in ('x86_64', 'aarch64'):
            raise Reject('E_CONFIG', failed=True)
        # Walk root ancestors without following even an intermediate symlink.
        if not os.path.isabs(self.root) or os.path.normpath(self.root) != self.root:
            raise Reject('E_PATH')
        fd = os.open('/', os.O_RDONLY | os.O_DIRECTORY)
        try:
            for part in self.root.strip('/').split('/'):
                if not part: continue
                try:
                    nxt = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fd)
                except OSError as e:
                    if e.errno in (errno.ELOOP, errno.ENOTDIR): raise Reject('E_FILE_LINK') from None
                    raise
                os.close(fd)
                fd = nxt
            self.fd = fd
            self.root_identity = identity(os.fstat(fd))
            return self
        except BaseException:
            os.close(fd)
            raise

    def __exit__(self, *_):
        if self.fd is not None: os.close(self.fd)

    def loc(self, path): return self.locations.get(path, Location(path))

    def open(self, path, directory=False):
        how = OpenHow(os.O_RDONLY | os.O_CLOEXEC | os.O_NOFOLLOW | os.O_NONBLOCK | (os.O_DIRECTORY if directory else 0), 0, 0x08 | 0x04)
        libc = ctypes.CDLL(None, use_errno=True)
        fd = libc.syscall(ctypes.c_long(437), ctypes.c_int(self.fd), ctypes.c_char_p(path.encode()), ctypes.byref(how), ctypes.sizeof(how))
        if fd < 0:
            err = ctypes.get_errno()
            if err in (errno.ELOOP, errno.EXDEV, errno.ENOENT, errno.ENOTDIR):
                raise Reject('E_INPUT_CHANGED', self.loc(path))
            if err in (errno.ENOSYS, errno.EINVAL): raise Reject('E_CONFIG', failed=True)
            raise OSError(err, 'openat2 failed')
        return fd

    def inventory(self):
        entries, folded = {}, {}
        def walk(fd, prefix=''):
            before = identity(os.fstat(fd))
            for name in sorted(os.listdir(fd), key=lambda s: s.encode('utf-8', errors='strict')):
                path = prefix + name
                loc = self.loc(path)
                if not inventory_path(path): raise Reject('E_PATH', loc)
                if path.casefold() in folded: raise Reject('E_PATH_COLLISION', loc, [self.loc(folded[path.casefold()])])
                folded[path.casefold()] = path
                st = os.stat(name, dir_fd=fd, follow_symlinks=False)
                if stat.S_ISLNK(st.st_mode): raise Reject('E_FILE_LINK', loc)
                if not stat.S_ISREG(st.st_mode) and not stat.S_ISDIR(st.st_mode): raise Reject('E_FILE_TYPE', loc)
                if stat.S_ISREG(st.st_mode) and st.st_nlink != 1: raise Reject('E_FILE_LINK', loc)
                entries[path] = identity(st)
                if stat.S_ISDIR(st.st_mode):
                    child = self.open(path, directory=True)
                    try:
                        if identity(os.fstat(child)) != entries[path]: raise Reject('E_INPUT_CHANGED', loc)
                        walk(child, path + '/')
                    finally: os.close(child)
            if before != identity(os.fstat(fd)): raise Reject('E_INPUT_CHANGED')
        walk(self.fd)
        return entries

    def read(self, path, expected, limit):
        if self.hook: self.hook('before_open', path)
        fd = self.open(path)
        try:
            before = os.fstat(fd)
            if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or identity(before) != expected:
                raise Reject('E_INPUT_CHANGED', self.loc(path))
            if before.st_size > limit: raise Reject('E_LIMIT', self.loc(path))
            data = bytearray()
            while True:
                block = os.read(fd, min(65536, limit + 1 - len(data)))
                if not block: break
                data.extend(block)
                if len(data) > limit: raise Reject('E_LIMIT', self.loc(path))
                if self.hook: self.hook('after_read', path)
            if identity(os.fstat(fd)) != expected: raise Reject('E_INPUT_CHANGED', self.loc(path))
            return bytes(data)
        finally: os.close(fd)

    def capture(self, entries, paths):
        total, data = 0, {}
        for path, limit in sorted(paths.items()):
            if path not in entries: raise Reject('E_FILE_MISSING', self.loc(path))
            if not stat.S_ISREG(entries[path][2]): raise Reject('E_FILE_TYPE', self.loc(path))
            if total + entries[path][4] > self.limits['package']: raise Reject('E_LIMIT', self.loc(path))
            data[path] = self.read(path, entries[path], min(limit, self.limits['package'] - total))
            total += len(data[path])
        # Verify the complete tree and reread bounded bytes through root-relative descriptors.
        # Retained immutable bytes are the compilation source after this boundary.
        if self.hook: self.hook('before_verify', '')
        if entries != self.inventory() or identity(os.stat(self.root, follow_symlinks=False)) != self.root_identity:
            raise Reject('E_INPUT_CHANGED')
        for path, content in data.items():
            if self.read(path, entries[path], paths[path]) != content: raise Reject('E_INPUT_CHANGED', self.loc(path))
        if entries != self.inventory(): raise Reject('E_INPUT_CHANGED')
        return data
