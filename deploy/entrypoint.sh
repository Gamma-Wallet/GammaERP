#!/bin/sh
# Brings the business schema to the image's migrations (unless MIGRATE_ON_START=false), then starts the API.
# The API migrates its own sign-in tables itself. The connection string comes from the host, never from the image.
set -e
if [ -z "$ConnectionStrings__Erp" ]; then
  echo "Set ConnectionStrings__Erp." >&2
  exit 1
fi
if [ "${MIGRATE_ON_START:-true}" = "true" ]; then
  /app/migrate --connection "$ConnectionStrings__Erp"
fi
exec dotnet /app/Gamma.Erp.Api.dll
