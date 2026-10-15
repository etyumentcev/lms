#!/bin/sh
set -eu
umask 077

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_dir=$(CDPATH= cd -- "$script_dir/../.." && pwd)
local_dir="$script_dir/.local"
mkdir -p "$local_dir/packages"

password_file="$local_dir/password"
if [ ! -s "$password_file" ]; then
  password="Pilot-$(openssl rand -hex 12)-Aa!"
  printf '%s\n' "$password" > "$password_file"
  chmod 0600 "$password_file"
fi

python3 "$script_dir/create_packages.py" "$script_dir/manifest.json" "$local_dir/packages"

node_bin=${LMS_BROWSER_NODE:-/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node}
playwright_module=${LMS_PLAYWRIGHT_MODULE:-/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright}
browser_executable=${LMS_BROWSER_EXECUTABLE:-/usr/bin/google-chrome}
content_python=${LMS_CONTENT_PYTHON:-/usr/bin/python3.14}
content_katex=${LMS_CONTENT_KATEX:-$repo_dir/src/Lms.Web/node_modules/katex}
proxy_dir=${LMS_CONTENT_PROXY_DIR:-/tmp/sapientum-lms-import-proxy}
proxy_socket="$proxy_dir/import.sock"
proxy_unit=sapientum-lms-import-proxy.service

api_container=$(docker compose --project-directory "$repo_dir" ps -q api)
if [ -z "$api_container" ]; then
  printf '%s\n' "[pilot-seed] API container is not running" >&2
  exit 1
fi
api_pid=$(docker inspect --format '{{.State.Pid}}' "$api_container")
api_host_uid=$(stat -c '%u' "/proc/$api_pid")
mkdir -p "$proxy_dir"
chmod 0711 "$proxy_dir"
systemctl --user stop "$proxy_unit" >/dev/null 2>&1 || true
systemctl --user reset-failed "$proxy_unit" >/dev/null 2>&1 || true
rm -f "$proxy_socket"
systemd-run --user --quiet --collect --unit="$proxy_unit" --service-type=exec \
  --property=KillMode=control-group --property=NoNewPrivileges=yes \
  --setenv="PYTHONPATH=$repo_dir" \
  --working-directory="$repo_dir" \
  "$content_python" -B "$repo_dir/tools/content_import/local_compose_proxy.py" \
  --socket "$proxy_socket" --node "$node_bin" --katex "$content_katex" \
  --allowed-peer-uid "$api_host_uid"
attempt=0
while [ ! -S "$proxy_socket" ]; do
  attempt=$((attempt + 1))
  if [ "$attempt" -ge 100 ] || ! systemctl --user is-active --quiet "$proxy_unit"; then
    printf '%s\n' "[pilot-seed] local content import proxy did not start" >&2
    exit 1
  fi
  sleep 0.1
done

platform_token=$(docker compose --project-directory "$repo_dir" --profile tools run --rm operator-token)
export PILOT_PASSWORD="$(sed -n '1p' "$password_file")"
export LMS_PLATFORM_TOKEN="$platform_token"
export LMS_PLAYWRIGHT_MODULE="$playwright_module"
export LMS_BROWSER_EXECUTABLE="$browser_executable"

exec "$node_bin" "$script_dir/seed.mjs"
