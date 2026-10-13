"""Strict ZIP transport, not a general archive extractor. Executed in the worker."""
import io
import os
from pathlib import Path
import re
import stat
import tarfile
import struct
import zipfile
import zlib
from tools.content_compiler.model import canonical, digest, REVISION
from .model import Denied


def path_parts(name, directory=False):
    if not isinstance(name, str) or not name or len(name.encode('utf-8')) > 1024:
        raise Denied('I_PATH')
    if directory:
        name = name[:-1] if name.endswith('/') else name
    parts = name.split('/')
    if len(parts) > 32 or any(p in ('', '.', '..') for p in parts):
        raise Denied('I_PATH')
    if any(c in name for c in '\\:%\x00?#') or any(ord(c) < 32 or ord(c) == 127 for c in name):
        raise Denied('I_PATH')
    if any(len(p.encode('utf-8')) > 255 for p in parts):
        raise Denied('I_PATH')
    return parts


def is_archive(name, data):
    old_tar = False
    if len(data) >= 512 and any(data[:512]):
        try:
            tarfile.TarInfo.frombuf(bytes(data[:512]), 'utf-8', 'strict')
            old_tar = True
        except (tarfile.HeaderError, UnicodeError):
            pass
    return (old_tar or zipfile.is_zipfile(io.BytesIO(data))) or (Path(name).suffix.lower() in {'.zip','.jar','.apk','.tar','.gz','.tgz','.bz2','.xz','.7z','.rar'}
            or data.startswith((b'PK\x03\x04', b'PK\x05\x06', b'PK\x07\x08', b'\x1f\x8b', b'BZh', b'\xfd7zXZ\0', b'7z\xbc\xaf\x27\x1c', b'Rar!'))
            or data[257:262] == b'ustar')


def extract(data, destination, limits):
    """Return canonical file index; all paths/metadata checked before first write."""
    if len(data) > limits.zip_bytes:
        raise Denied('I_ZIP_LIMIT')
    # Deliberately narrow: single disk, no ZIP64, prefix, trailing bytes or comments.
    # Preflight EOCD BEFORE ZipFile allocates a list for a malicious directory.
    if len(data) < 22 or data[-22:-18] != b'PK\x05\x06':
        raise Denied('I_ZIP_FORMAT')
    disk, cd_disk, disk_count, count, cd_size, cd_offset, comment = struct.unpack_from('<4H2IH', data, len(data)-18)
    if disk or cd_disk or comment or disk_count != count or count == 65535 or cd_offset+cd_size != len(data)-22:
        raise Denied('I_ZIP_FORMAT')
    if count > limits.entries:
        raise Denied('I_ENTRY_LIMIT')
    cursor, actual_count = cd_offset, 0
    while cursor < cd_offset+cd_size:
        if data[cursor:cursor+4] != b'PK\x01\x02' or cursor+46 > len(data)-22:
            raise Denied('I_ZIP_FORMAT')
        n, e, c = struct.unpack_from('<HHH', data, cursor+28)
        cursor += 46+n+e+c; actual_count += 1
        if actual_count > limits.entries:
            raise Denied('I_ENTRY_LIMIT')
    if cursor != cd_offset+cd_size or actual_count != count:
        raise Denied('I_ZIP_FORMAT')
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            planned, names, nodes = [], set(), {}
            offset = 0
            for info in z.infolist():
                directory = info.is_dir()
                parts = path_parts(info.orig_filename, directory)
                name = '/'.join(parts); folded = name.casefold()
                if folded in names:
                    raise Denied('I_COLLISION')
                names.add(folded)
                for i in range(1, len(parts)+1):
                    original = '/'.join(parts[:i]); key = original.casefold()
                    kind = 'dir' if i < len(parts) or directory else 'file'
                    if key in nodes and nodes[key] != (original, kind):
                        raise Denied('I_COLLISION')
                    nodes[key] = (original, kind)
                mode = info.external_attr >> 16
                if info.create_system not in (0, 3) or stat.S_IFMT(mode) not in (0, stat.S_IFDIR if directory else stat.S_IFREG):
                    raise Denied('I_ENTRY_TYPE')
                # Reject all extension records: includes Unix link payloads, ZIP64,
                # conflicting Unicode paths and metadata not needed by this profile.
                if info.extra or info.comment or info.flag_bits & ~0x808 or info.compress_type not in (0, 8):
                    raise Denied('I_ZIP_FEATURE')
                if info.header_offset != offset or data[offset:offset+4] != b'PK\x03\x04':
                    raise Denied('I_ZIP_FORMAT')
                flags, method = struct.unpack_from('<HH', data, offset+6)
                n,e = struct.unpack_from('<HH', data, offset+26)
                if flags != info.flag_bits or method != info.compress_type or e:
                    raise Denied('I_ZIP_FEATURE')
                payload_start=offset+30+n+e
                local_name=data[offset+30:offset+30+n].decode('utf-8' if flags & 0x800 else 'cp437')
                if local_name != info.orig_filename: raise Denied('I_ZIP_FORMAT')
                local_values=struct.unpack_from('<III',data,offset+14)
                if not flags & 8 and local_values != (info.CRC,info.compress_size,info.file_size):
                    raise Denied('I_ZIP_FORMAT')
                offset = payload_start+info.compress_size
                if flags & 8:
                    signed = data[offset:offset+4] == b'PK\x07\x08'
                    pos = offset + (4 if signed else 0)
                    if struct.unpack_from('<III', data, pos) != (info.CRC, info.compress_size, info.file_size):
                        raise Denied('I_ZIP_FORMAT')
                    offset = pos+12
                if offset > cd_offset:
                    raise Denied('I_ZIP_FORMAT')
                if directory and (info.file_size or info.compress_size):
                    raise Denied('I_ZIP_FEATURE')
                if info.file_size > limits.expanded_bytes:
                    raise Denied('I_EXPANDED_LIMIT')
                planned.append((info,name,directory,payload_start))
            if offset != cd_offset:
                raise Denied('I_ZIP_FORMAT')
            total, index = 0, []
            for info,name,directory,payload_start in planned:
                target = destination.joinpath(*name.split('/'))
                if directory:
                    target.mkdir(parents=True, exist_ok=True, mode=0o700)
                    continue
                target.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
                content = bytearray()
                # ZipExtFile truncates to declared file_size. Decode the raw
                # stream instead, limiting every inflate call by actual bytes.
                decoder=zlib.decompressobj(-15) if info.compress_type == 8 else None
                cursor=payload_start; end=cursor+info.compress_size; pending=b''; crc=0
                while cursor < end or pending:
                    if not pending:
                        pending=data[cursor:min(cursor+65536,end)]; cursor+=len(pending)
                    if decoder:
                        block=decoder.decompress(pending,min(65536,limits.expanded_bytes-total+1))
                        pending=decoder.unconsumed_tail
                        if decoder.unused_data or decoder.eof and (pending or cursor<end):
                            raise Denied('I_ZIP_FORMAT')
                    else:
                        block=pending;pending=b''
                    total+=len(block)
                    if total>limits.expanded_bytes: raise Denied('I_EXPANDED_LIMIT')
                    crc=zlib.crc32(block,crc);content.extend(block)
                if decoder and not decoder.eof or crc != info.CRC: raise Denied('I_ZIP_FORMAT')
                if len(content) != info.file_size: raise Denied('I_ZIP_FORMAT')
                if is_archive(name, content): raise Denied('I_NESTED_ARCHIVE')
                with open(target, 'xb') as out: out.write(content)
                os.chmod(target, 0o400)
                index.append(dict(path=name, sizeBytes=len(content), sha256=digest(content)))
            return sorted(index, key=lambda x:x['path'])
    except (zipfile.BadZipFile, NotImplementedError, RuntimeError, UnicodeError, struct.error, EOFError, ValueError, zlib.error):
        raise Denied('I_ZIP_FORMAT') from None


def package_hash(index):
    return digest(canonical(dict(contractRevision=REVISION, files=index)))
