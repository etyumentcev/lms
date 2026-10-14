#!/bin/sh
set -eu

export PGPASSWORD="$(cat /run/lms-secrets/postgres-password)"
keycloak_password="$(cat /run/lms-secrets/keycloak-db-password)"

psql --set=ON_ERROR_STOP=1 --set=keycloak_password="$keycloak_password" <<'SQL'
SELECT format('CREATE ROLE keycloak LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', :'keycloak_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'keycloak')
\gexec
ALTER ROLE keycloak PASSWORD :'keycloak_password';
SELECT 'CREATE DATABASE keycloak OWNER keycloak'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'keycloak')
\gexec
SQL
