#!/bin/sh
set -eu

secret_path=/run/lms-secrets/admin-entry-uuid
if [ ! -r "$secret_path" ]; then
  echo "Sapientum web admin entry secret is unavailable" >&2
  exit 1
fi
admin_entry_uuid=$(tr -d "\r\n" < "$secret_path")
if ! printf "%s" "$admin_entry_uuid" | grep -Eq "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-4[0-9a-fA-F]{3}-[89aAbB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$"; then
  echo "Sapientum web admin entry secret is invalid" >&2
  exit 1
fi
export ADMIN_ENTRY_UUID="$admin_entry_uuid"
envsubst "\${ADMIN_ENTRY_UUID}" < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf
unset admin_entry_uuid ADMIN_ENTRY_UUID
exec nginx -g "daemon off;"
