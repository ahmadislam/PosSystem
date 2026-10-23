#!/usr/bin/env bash
set -euo pipefail
umask 077
script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source "$script_directory/postgres-tools.sh"

if [[ $# -ne 2 || "${RESTORE_TARGET_CONFIRMED:-}" != 1 ]]; then
  echo 'Usage: RESTORE_TARGET_CONFIRMED=1 ./scripts/restore-state.sh <bundle-directory> <new-or-empty-image-directory>' >&2
  echo 'Select a disposable/empty database and stop application/job processes before restoring.' >&2
  exit 2
fi
target="$(pg_target)"
bundle="$(realpath -- "$1")"
python3 "$script_directory/backup-manifest.py" verify "$bundle"
destination="$(realpath -m -- "$2")"
[[ ! -L "$2" ]] || { echo 'Image target must not be a symbolic link.' >&2; exit 2; }
case "$destination/" in "$bundle/"*) echo 'Restore destination cannot be inside the backup bundle.' >&2; exit 2;; esac
if [[ -e "$destination" ]]; then
  [[ -d "$destination" && -z "$(ls -A -- "$destination")" ]] || { echo 'Image restore target must be absent or empty.' >&2; exit 2; }
fi
relations="$(run_pg psql --no-psqlrc --tuples-only --no-align --dbname="$target" --command="SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp%';")"
[[ "$relations" == 0 ]] || { echo 'Database restore target contains objects; refusing destructive replacement.' >&2; exit 2; }

mkdir -p -- "$(dirname -- "$destination")"
temporary="$(mktemp -d "$(dirname -- "$destination")/.pos-images.XXXXXX")"
trap 'rm -rf -- "$temporary"' EXIT
cp -a -- "$bundle/product-images/." "$temporary/"
run_pg pg_restore --exit-on-error --single-transaction --no-owner --no-acl --dbname="$target" < "$bundle/database.dump"
if [[ -d "$destination" ]]; then rmdir -- "$destination"; fi
mv -- "$temporary" "$destination"
echo 'Database and images restored. Keep traffic stopped until migrations, health, login and image downloads are verified.'
