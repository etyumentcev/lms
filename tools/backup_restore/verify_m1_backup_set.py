#!/usr/bin/env python3
"""Fail-closed structural and checksum validation for an M1 synthetic backup set."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from datetime import datetime
from pathlib import Path, PurePosixPath


FAIL_EXIT = 42
SHA256 = re.compile(r"^[0-9a-f]{64}$")
HEAD = re.compile(r"^[0-9a-f]{40}$")


def digest(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def parse_utc(value: object, field: str) -> datetime:
    require(isinstance(value, str) and value.endswith("Z"), f"{field} must be an ISO-8601 UTC value ending in Z")
    try:
        parsed = datetime.fromisoformat(value[:-1] + "+00:00")
    except ValueError as error:
        raise ValueError(f"{field} is not valid ISO-8601: {error}") from error
    require(parsed.utcoffset() is not None and parsed.utcoffset().total_seconds() == 0, f"{field} must be UTC")
    return parsed


def safe_relative(value: object, field: str) -> PurePosixPath:
    require(isinstance(value, str) and value, f"{field} must be a non-empty relative path")
    candidate = PurePosixPath(value)
    require(not candidate.is_absolute() and ".." not in candidate.parts, f"{field} must stay inside the backup set")
    return candidate


def validate(root: Path, manifest_path: Path) -> dict[str, object]:
    require(root.is_dir(), "backup set root does not exist")
    require(manifest_path.is_file(), "manifest is missing")
    require(not manifest_path.is_symlink(), "manifest must not be a symlink")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

    require(manifest.get("schemaVersion") == 1, "schemaVersion must be 1")
    require(isinstance(manifest.get("backupId"), str) and manifest["backupId"], "backupId is required")
    require(manifest.get("syntheticOnly") is True, "syntheticOnly must be true")
    build = manifest.get("build")
    require(isinstance(build, dict) and HEAD.fullmatch(str(build.get("head", ""))) is not None, "build.head must be a 40-character commit SHA")

    capture = manifest.get("capture")
    require(isinstance(capture, dict), "capture object is required")
    require(capture.get("timeZone") == "UTC", "capture.timeZone must be UTC")
    started = parse_utc(capture.get("startedAt"), "capture.startedAt")
    completed = parse_utc(capture.get("completedAt"), "capture.completedAt")
    require(completed >= started, "capture.completedAt precedes capture.startedAt")

    migrations = manifest.get("migrations")
    require(isinstance(migrations, list) and migrations, "migrations must be a non-empty list")
    require(all(isinstance(item, str) and item for item in migrations), "every migration id must be non-empty")

    database = manifest.get("postgresqlBackup")
    require(isinstance(database, dict), "postgresqlBackup object is required")
    database_path = root / safe_relative(database.get("relativePath"), "postgresqlBackup.relativePath")
    require(database_path.is_file() and not database_path.is_symlink(), "PostgreSQL backup payload is missing or is a symlink")
    require(database.get("format") == "custom", "PostgreSQL backup format must be custom")
    require(isinstance(database.get("size"), int) and database["size"] >= 0, "PostgreSQL backup size is invalid")
    require(SHA256.fullmatch(str(database.get("sha256", ""))) is not None, "PostgreSQL backup SHA-256 is invalid")
    require(database_path.stat().st_size == database["size"], "PostgreSQL backup size mismatch")
    require(digest(database_path) == database["sha256"], "PostgreSQL backup SHA-256 mismatch")

    snapshot = manifest.get("privateFilesSnapshot")
    require(isinstance(snapshot, dict), "privateFilesSnapshot object is required")
    private_root = root / safe_relative(snapshot.get("relativeRoot"), "privateFilesSnapshot.relativeRoot")
    require(private_root.is_dir() and not private_root.is_symlink(), "private-files snapshot root is missing or is a symlink")
    files = snapshot.get("files")
    require(isinstance(files, list), "privateFilesSnapshot.files must be a list")

    records: list[tuple[str, str]] = []
    seen: set[str] = set()
    total_bytes = 0
    for index, item in enumerate(files):
        require(isinstance(item, dict), f"privateFilesSnapshot.files[{index}] must be an object")
        relative = safe_relative(item.get("path"), f"privateFilesSnapshot.files[{index}].path")
        key = relative.as_posix()
        require(key not in seen, f"duplicate private file path: {key}")
        seen.add(key)
        file_path = private_root / relative
        require(file_path.is_file() and not file_path.is_symlink(), f"private file is missing or is a symlink: {key}")
        require(isinstance(item.get("size"), int) and item["size"] >= 0, f"invalid private file size: {key}")
        require(SHA256.fullmatch(str(item.get("sha256", ""))) is not None, f"invalid private file SHA-256: {key}")
        require(file_path.stat().st_size == item["size"], f"private file size mismatch: {key}")
        require(digest(file_path) == item["sha256"], f"private file SHA-256 mismatch: {key}")
        total_bytes += item["size"]
        records.append((key, f"{item['sha256']}  {key}\n"))

    snapshot_entries = list(private_root.rglob("*"))
    symlinks = [path.relative_to(private_root).as_posix() for path in snapshot_entries if path.is_symlink()]
    require(not symlinks, f"private-files snapshot contains symlinks: {symlinks}")
    actual_paths = {
        path.relative_to(private_root).as_posix()
        for path in snapshot_entries
        if path.is_file()
    }
    require(actual_paths == seen, "private-files manifest does not exactly cover snapshot payload")
    aggregate = hashlib.sha256("".join(record for _, record in sorted(records)).encode("utf-8")).hexdigest()
    require(snapshot.get("fileCount") == len(files), "private-files fileCount mismatch")
    require(snapshot.get("totalBytes") == total_bytes, "private-files totalBytes mismatch")
    require(snapshot.get("aggregateSha256") == aggregate, "private-files aggregate SHA-256 mismatch")

    consistency = manifest.get("consistency")
    require(isinstance(consistency, dict), "consistency object is required")
    require(consistency.get("writersQuiesced") is True, "manifest does not assert a quiesced writer barrier")
    require(isinstance(consistency.get("method"), str) and consistency["method"], "consistency.method is required")

    return {
        "status": "PASS",
        "phase": "pre-restore-validation",
        "backupId": manifest["backupId"],
        "manifest": str(manifest_path),
        "manifestSha256": digest(manifest_path),
        "databaseSha256": database["sha256"],
        "privateFilesAggregateSha256": aggregate,
        "migrations": len(migrations),
        "files": len(files),
        "bytes": total_bytes,
        "mutationsPerformed": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("backup_set", type=Path)
    parser.add_argument("--manifest", type=Path)
    arguments = parser.parse_args()
    root = arguments.backup_set.resolve()
    manifest = (arguments.manifest or root / "manifest.json").resolve()
    try:
        result = validate(root, manifest)
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(json.dumps({
            "status": "REFUSED",
            "phase": "pre-restore-validation",
            "reason": str(error),
            "mutationsPerformed": False,
        }, ensure_ascii=False, indent=2))
        return FAIL_EXIT
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
