"""Fixed server adapter for the accepted B07b-C1 importer.

Arguments are supplied only by trusted application configuration.  The uploaded
ZIP remains an opaque path chosen by the server; archive content never controls
the process command or environment.
"""
from dataclasses import asdict
import base64
import json
from pathlib import Path
import sys

from tools.content_import.importer import import_zip
from tools.content_import.sandbox import Runtime


def artifact(value):
    if value is None:
        return None
    return {
        "json": base64.b64encode(value.content).decode("ascii"),
        "assets": {path: base64.b64encode(data).decode("ascii") for path, data in value.assets},
    }


def main():
    if len(sys.argv) != 5:
        raise SystemExit(64)
    result = import_zip(
        Path(sys.argv[1]),
        runtime=Runtime(Path(sys.argv[2]), Path(sys.argv[3])),
        source_revision=sys.argv[4],
    )
    payload = {
        "status": result.status,
        "diagnostics": list(result.diagnostics),
        "receipt": asdict(result.receipt) if result.receipt else None,
        "student": artifact(result.student),
        "teacher": artifact(result.teacher),
    }
    sys.stdout.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")))


if __name__ == "__main__":
    main()
