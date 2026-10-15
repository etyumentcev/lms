#!/bin/sh
set -eu
umask 077

root=/run/lms-secrets
mkdir -p "$root"

secret() {
  path="$root/$1"
  [ -s "$path" ] || openssl rand -hex 32 > "$path"
}

secret postgres-password
secret keycloak-db-password
secret keycloak-admin-password
secret platform-operator-password
[ -s "$root/challenge-key.b64" ] || openssl rand -base64 32 > "$root/challenge-key.b64"
[ -s "$root/admin-entry-uuid" ] || openssl rand -hex 16 | sed -E 's|^(.{8})(.{4}).(.{3}).(.{3})(.{12})$|\1-\2-4\3-8\4-\5|' > "$root/admin-entry-uuid"
[ -s "$root/component-signing.pem" ] || openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$root/component-signing.pem"

if [ ! -s "$root/ca.crt" ] || [ ! -s "$root/ca.key" ]; then
  openssl req -x509 -newkey rsa:3072 -nodes -sha256 -days 365 \
    -subj '/CN=Sapientum Local Synthetic CA' -keyout "$root/ca.key" -out "$root/ca.crt"
fi

certificate() {
  name="$1"
  dns="$2"
  if [ ! -s "$root/$name.crt" ] || [ ! -s "$root/$name.key" ]; then
    openssl req -newkey rsa:2048 -nodes -sha256 -subj "/CN=$dns" \
      -keyout "$root/$name.key" -out "$root/$name.csr"
    printf 'subjectAltName=DNS:%s\nextendedKeyUsage=serverAuth\n' "$dns" > "$root/$name.ext"
    if [ "$dns" = localhost ]; then
      printf 'subjectAltName=DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\n' > "$root/$name.ext"
    fi
    openssl x509 -req -sha256 -days 365 -in "$root/$name.csr" \
      -CA "$root/ca.crt" -CAkey "$root/ca.key" -CAcreateserial \
      -extfile "$root/$name.ext" -out "$root/$name.crt"
    rm -f "$root/$name.csr" "$root/$name.ext"
  fi
}

certificate web localhost
certificate api api
certificate identity identity

if [ ! -s "$root/sapientum-local-realm.json" ]; then
  operator_password=$(cat "$root/platform-operator-password")
  sed "s/__PLATFORM_OPERATOR_PASSWORD__/$operator_password/g" \
    /opt/lms/sapientum-local-realm.template.json > "$root/sapientum-local-realm.json"
fi

chmod 0444 "$root"/*.crt "$root"/sapientum-local-realm.json
chmod 0444 "$root"/*.key "$root"/*.pem "$root"/*-password "$root"/challenge-key.b64 "$root"/admin-entry-uuid
