#!/usr/bin/env bash
set -euo pipefail
script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source "$script_directory/postgres-tools.sh"
temporary="$(mktemp -d)"
source_database="pos_backup_test_${$}"
restore_database="pos_restore_test_${$}"
cleanup() {
  run_pg dropdb --if-exists "$source_database" >/dev/null
  run_pg dropdb --if-exists "$restore_database" >/dev/null
  rm -rf -- "$temporary"
}
trap cleanup EXIT
run_pg createdb "$source_database"
run_pg createdb "$restore_database"
mkdir "$temporary/source-images"
printf 'test-image-bytes\n' > "$temporary/source-images/fixture.png"
run_pg psql --no-psqlrc --set=ON_ERROR_STOP=1 --dbname="$source_database" --command="CREATE TABLE backup_fixture(id int PRIMARY KEY, storage_key text NOT NULL); INSERT INTO backup_fixture VALUES (1,'fixture.png');" >/dev/null
DATABASE_URL= PGDATABASE="$source_database" BACKUP_WRITES_PAUSED=1 PRODUCT_IMAGES_DIRECTORY="$temporary/source-images" "$script_directory/backup-state.sh" "$temporary/bundle"
DATABASE_URL= PGDATABASE="$restore_database" RESTORE_TARGET_CONFIRMED=1 "$script_directory/restore-state.sh" "$temporary/bundle" "$temporary/restored-images"
restored="$(run_pg psql --no-psqlrc --tuples-only --no-align --dbname="$restore_database" --command='SELECT storage_key FROM backup_fixture WHERE id=1;')"
[[ "$restored" == fixture.png ]]
cmp "$temporary/source-images/fixture.png" "$temporary/restored-images/fixture.png"
if DATABASE_URL= PGDATABASE="$restore_database" RESTORE_TARGET_CONFIRMED=1 "$script_directory/restore-state.sh" "$temporary/bundle" "$temporary/second-images" >/dev/null 2>&1; then
  echo 'ERROR: nonempty restore target was accepted.' >&2; exit 1
fi
printf 'corrupted' >> "$temporary/bundle/product-images/fixture.png"
if python3 "$script_directory/backup-manifest.py" verify "$temporary/bundle" >/dev/null 2>&1; then
  echo 'ERROR: corrupted backup was accepted.' >&2; exit 1
fi
echo 'PASS: database row and image bytes restored; nonempty target and corrupted backup rejected.'
