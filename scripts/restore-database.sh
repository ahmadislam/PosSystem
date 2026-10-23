#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${DATABASE_URL:-}" ]]; then
  echo "Set DATABASE_URL to the PostgreSQL restore target." >&2
  exit 2
fi
if [[ $# -ne 1 || ! -f "$1" ]]; then
  echo "Usage: DATABASE_URL=... $0 <existing-backup-file.dump>" >&2
  exit 2
fi

echo "This replaces database objects in the target. Use a disposable database."
read -r -p "Type RESTORE to continue: " confirmation
if [[ "$confirmation" != "RESTORE" ]]; then
  echo "Restore cancelled."
  exit 1
fi
pg_restore --clean --if-exists --no-owner --no-acl --dbname="$DATABASE_URL" "$1"
echo "Restore completed. Run application migrations before serving traffic."
