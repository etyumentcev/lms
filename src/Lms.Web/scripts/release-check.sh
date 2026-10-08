#!/usr/bin/env bash
# Isolated, destructive only within lms-k-smoke; never use a user-data Compose project.
set -euo pipefail
cd "$(dirname "$0")/../../.."
: "${LMS_PLAYWRIGHT_MODULE:=playwright}"
export LMS_PLAYWRIGHT_MODULE
export LMS_SMOKE_URL=http://127.0.0.1:8086
export LMS_SMOKE_OUTPUT="${LMS_K_OUTPUT:-/tmp/lms-k-run}/browser"
export LMS_PERF_OUTPUT="${LMS_K_OUTPUT:-/tmp/lms-k-run}/performance"
mkdir -p "${LMS_K_OUTPUT:-/tmp/lms-k-run}"
export COMPOSE_FILE="${LMS_K_OUTPUT:-/tmp/lms-k-run}/compose.yml"
python3 - <<'PY'
from pathlib import Path
import os
root=Path.cwd()
s=(root/'docker-compose.yml').read_text().replace('name: lms-local','name: lms-k-smoke').replace('context: .',f'context: {root}').replace('127.0.0.1:8080:80','127.0.0.1:8086:80')
Path(os.environ['COMPOSE_FILE']).write_text(s)
PY
# Refuse to take over an already running project, even with the reserved test name.
if test -n "$(docker compose ps -aq)"; then
  echo 'lms-k-smoke already exists. Inspect it and explicitly clean up the disposable test stack before rerunning.' >&2
  exit 1
fi
cleanup() { docker compose down -v > "${LMS_K_OUTPUT:-/tmp/lms-k-run}/cleanup.log" 2>&1; }
trap cleanup EXIT
docker compose up --build -d --wait > "${LMS_K_OUTPUT:-/tmp/lms-k-run}/compose.log" 2>&1
node src/Lms.Web/scripts/release-smoke.mjs > "${LMS_K_OUTPUT:-/tmp/lms-k-run}/browser.log" 2>&1
LMS_SMOKE_OUTPUT="${LMS_K_OUTPUT:-/tmp/lms-k-run}/tablet" node src/Lms.Web/scripts/annotation-tablet-smoke.mjs > "${LMS_K_OUTPUT:-/tmp/lms-k-run}/tablet.log" 2>&1
docker compose down -v > "${LMS_K_OUTPUT:-/tmp/lms-k-run}/reset.log" 2>&1
docker compose up -d --wait >> "${LMS_K_OUTPUT:-/tmp/lms-k-run}/reset.log" 2>&1
node src/Lms.Web/scripts/release-performance.mjs > "${LMS_K_OUTPUT:-/tmp/lms-k-run}/performance.log" 2>&1
docker stats --no-stream --format '{{.Name}} {{.MemUsage}}' lms-k-smoke-api-1 lms-k-smoke-postgres-1 > "${LMS_K_OUTPUT:-/tmp/lms-k-run}/memory.txt"
echo "K browser, emulated tablet, performance: PASS. Evidence: ${LMS_K_OUTPUT:-/tmp/lms-k-run}"
