#!/usr/bin/env bash
set -euo pipefail
script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source "$script_directory/postgres-tools.sh"

target="$(pg_target)"
if [[ $# -ne 1 || ! -f "$1" ]]; then
  echo "Usage: DATABASE_URL=... $0 <existing-backup-file.dump>" >&2
  exit 2
fi

relations="$(run_pg psql --no-psqlrc --tuples-only --no-align --dbname="$target" --command="SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp%';")"
[[ "$relations" == 0 ]] || { echo 'Database target contains objects; restore to an empty database.' >&2; exit 2; }
echo "Restore to the selected empty database. Image files are not included in a database-only dump."
read -r -p "Type RESTORE to continue: " confirmation
if [[ "$confirmation" != "RESTORE" ]]; then
  echo "Restore cancelled."
  exit 1
fi
run_pg pg_restore --exit-on-error --single-transaction --no-owner --no-acl --dbname="$target" < "$1"
echo "Restore completed. Run application migrations before serving traffic."
