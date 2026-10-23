#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${DATABASE_URL:-}" ]]; then
  echo "Set DATABASE_URL to the PostgreSQL connection string." >&2
  exit 2
fi
if [[ $# -ne 1 ]]; then
  echo "Usage: DATABASE_URL=... $0 <backup-file.dump>" >&2
  exit 2
fi

backup_file="$1"
mkdir -p "$(dirname "$backup_file")"
pg_dump --format=custom --no-owner --no-acl --file="$backup_file" "$DATABASE_URL"
echo "Database backup written to $backup_file"
