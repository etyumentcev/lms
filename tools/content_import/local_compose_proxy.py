"""Local SyntheticOnly bridge from the Compose API to the accepted B07b sandbox.

The public API never controls executable paths or filesystem paths.  A framed
Unix-socket request contains only a source revision and ZIP bytes.  Peer
credentials restrict the socket to the configured rootless-container UID.
"""

from __future__ import annotations

import argparse
from dataclasses import asdict
import base64
import json
import os
from pathlib import Path
import shutil
import socket
import struct
import tempfile

from tools.content_import.importer import import_zip
from tools.content_import.sandbox import Runtime


MAX_HEADER_BYTES = 4096
MAX_ZIP_BYTES = 100_000_000
MAX_RESPONSE_BYTES = 64_000_000


def receive_exact(connection: socket.socket, length: int) -> bytes:
    value = bytearray()
    while len(value) < length:
        block = connection.recv(min(65536, length - len(value)))
        if not block:
            raise ValueError("Unexpected end of request")
        value.extend(block)
    return bytes(value)


def artifact(value):
    if value is None:
        return None
    return {
        "json": base64.b64encode(value.content).decode("ascii"),
        "assets": {path: base64.b64encode(data).decode("ascii") for path, data in value.assets},
    }


def failed_payload() -> bytes:
    return json.dumps(
        {
            "status": "Failed",
            "diagnostics": [
                {"severity": "ERROR", "code": "I_IMPORT", "message": "Local import did not complete."}
            ],
            "receipt": None,
            "student": None,
            "teacher": None,
        },
        separators=(",", ":"),
    ).encode()


def handle(connection: socket.socket, runtime: Runtime, allowed_peer_uid: int) -> None:
    credentials = connection.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, struct.calcsize("3i"))
    _, peer_uid, _ = struct.unpack("3i", credentials)
    if peer_uid != allowed_peer_uid:
        raise PermissionError("Unexpected peer")
    connection.settimeout(45)
    header_length = struct.unpack("!I", receive_exact(connection, 4))[0]
    if header_length <= 0 or header_length > MAX_HEADER_BYTES:
        raise ValueError("Invalid header length")
    header = json.loads(receive_exact(connection, header_length))
    if set(header) != {"sourceRevision", "zipLength"}:
        raise ValueError("Invalid header")
    revision = header["sourceRevision"]
    zip_length = header["zipLength"]
    if (
        type(revision) is not str
        or not 0 < len(revision.encode("utf-8")) <= 256
        or any(ord(character) < 32 for character in revision)
        or type(zip_length) is not int
        or not 0 < zip_length <= MAX_ZIP_BYTES
    ):
        raise ValueError("Invalid request")

    staging = Path(tempfile.mkdtemp(prefix="sapientum-compose-import-"))
    staging.chmod(0o700)
    try:
        source = staging / "input.zip"
        with source.open("xb") as stream:
            remaining = zip_length
            while remaining:
                block = connection.recv(min(65536, remaining))
                if not block:
                    raise ValueError("Unexpected end of ZIP")
                stream.write(block)
                remaining -= len(block)
        source.chmod(0o400)
        result = import_zip(source, runtime=runtime, source_revision=revision)
        payload = json.dumps(
            {
                "status": result.status,
                "diagnostics": list(result.diagnostics),
                "receipt": asdict(result.receipt) if result.receipt else None,
                "student": artifact(result.student),
                "teacher": artifact(result.teacher),
            },
            ensure_ascii=False,
            separators=(",", ":"),
        ).encode()
    finally:
        shutil.rmtree(staging, ignore_errors=True)

    if len(payload) > MAX_RESPONSE_BYTES:
        payload = failed_payload()
    connection.sendall(struct.pack("!I", len(payload)) + payload)


def serve(socket_path: Path, runtime: Runtime, allowed_peer_uid: int) -> None:
    socket_path.parent.mkdir(mode=0o711, parents=True, exist_ok=True)
    socket_path.parent.chmod(0o711)
    socket_path.unlink(missing_ok=True)
    server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    try:
        server.bind(str(socket_path))
        socket_path.chmod(0o666)
        server.listen(4)
        while True:
            connection, _ = server.accept()
            with connection:
                try:
                    handle(connection, runtime, allowed_peer_uid)
                except Exception:
                    try:
                        payload = failed_payload()
                        connection.sendall(struct.pack("!I", len(payload)) + payload)
                    except OSError:
                        pass
    finally:
        server.close()
        socket_path.unlink(missing_ok=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--socket", required=True, type=Path)
    parser.add_argument("--node", required=True, type=Path)
    parser.add_argument("--katex", required=True, type=Path)
    parser.add_argument("--allowed-peer-uid", required=True, type=int)
    arguments = parser.parse_args()
    os.umask(0o077)
    serve(arguments.socket, Runtime(arguments.node, arguments.katex), arguments.allowed_peer_uid)


if __name__ == "__main__":
    main()
