#!/usr/bin/env bash
#
# Creates one database and two principals per service for local development.
# Decisions: docs/adr/ADR-0004-database-per-service-and-tenant-isolation.md
#
#   <service>_migrator : owns the database and may change schema
#   <service>_app      : may connect to its own database and read/write data only
#
# Every other principal is denied CONNECT, so cross-service access fails even with
# valid credentials. Missing passwords abort initialisation: there are no fallback
# credentials anywhere in this script.

set -euo pipefail

services="
coreledger:core_ledger_db
customeronboarding:customer_onboarding_db
identityaccess:identity_access_db
paymentsvtu:payments_vtu_db
channelsengagement:channels_engagement_db
operationsworkflow:operations_workflow_db
riskreporting:risk_reporting_db
integrationhub:integration_hub_db
auditreconciliation:audit_reconciliation_db
"

admin="${POSTGRES_USER:-cb_admin}"

for entry in $services; do
  service="${entry%%:*}"
  database="${entry##*:}"
  prefix="$(echo "$service" | tr '[:lower:]' '[:upper:]')"

  app_password_name="${prefix}_APP_PASSWORD"
  migrator_password_name="${prefix}_MIGRATOR_PASSWORD"
  app_password="${!app_password_name:-}"
  migrator_password="${!migrator_password_name:-}"

  if [ -z "$app_password" ] || [ -z "$migrator_password" ]; then
    echo "FATAL: ${app_password_name} or ${migrator_password_name} is not set; refusing to create principals with fallback credentials." >&2
    exit 1
  fi

  app_role="${service}_app"
  migrator_role="${service}_migrator"

  psql -v ON_ERROR_STOP=1 --username "$admin" --dbname postgres <<SQL
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '${migrator_role}') THEN
    CREATE ROLE ${migrator_role} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '${app_role}') THEN
    CREATE ROLE ${app_role} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
  END IF;
END
\$\$;

ALTER ROLE ${migrator_role} WITH PASSWORD '${migrator_password}';
ALTER ROLE ${app_role} WITH PASSWORD '${app_password}';
SQL

  if ! psql -tAc "SELECT 1 FROM pg_database WHERE datname = '${database}'" --username "$admin" --dbname postgres | grep -q 1; then
    psql -v ON_ERROR_STOP=1 --username "$admin" --dbname postgres \
      -c "CREATE DATABASE ${database} OWNER ${migrator_role};"
  fi

  psql -v ON_ERROR_STOP=1 --username "$admin" --dbname postgres <<SQL
REVOKE CONNECT ON DATABASE ${database} FROM PUBLIC;
GRANT CONNECT ON DATABASE ${database} TO ${app_role};
GRANT CONNECT ON DATABASE ${database} TO ${migrator_role};
SQL

  psql -v ON_ERROR_STOP=1 --username "$admin" --dbname "$database" <<SQL
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO ${migrator_role};
GRANT USAGE ON SCHEMA public TO ${app_role};

ALTER DEFAULT PRIVILEGES FOR ROLE ${migrator_role} IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ${app_role};
ALTER DEFAULT PRIVILEGES FOR ROLE ${migrator_role} IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO ${app_role};
ALTER DEFAULT PRIVILEGES FOR ROLE ${migrator_role} IN SCHEMA public
  GRANT EXECUTE ON FUNCTIONS TO ${app_role};
SQL

  echo "created ${database} with ${migrator_role} as owner and ${app_role} as application principal"
done

echo "database initialisation complete"
