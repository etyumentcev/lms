#!/bin/sh
set -eu

password=$(cat /run/lms-secrets/platform-operator-password)
response=$(curl --fail --silent --show-error \
  --cacert /run/lms-secrets/ca.crt \
  --data-urlencode grant_type=password \
  --data-urlencode client_id=sapientum-platform-cli \
  --data-urlencode username=platform.operator \
  --data-urlencode "password=$password" \
  https://identity:8443/realms/sapientum-local/protocol/openid-connect/token)

printf '%s' "$response" | jq -er '.access_token'
