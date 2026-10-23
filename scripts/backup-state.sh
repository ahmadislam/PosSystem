#!/usr/bin/env bash
set -euo pipefail
umask 077
script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source "$script_directory/postgres-tools.sh"

if [[ $# -ne 1 || "${BACKUP_WRITES_PAUSED:-}" != 1 ]]; then
  echo 'Usage: BACKUP_WRITES_PAUSED=1 PRODUCT_IMAGES_DIRECTORY=/persistent/images ./scripts/backup-state.sh <new-bundle-directory>' >&2
  echo 'Stop application/job writes for the entire backup so database rows and files match.' >&2
  exit 2
fi
target="$(pg_target)"
images="${PRODUCT_IMAGES_DIRECTORY:?Set PRODUCT_IMAGES_DIRECTORY to the API image directory}"
[[ -d "$images" && ! -L "$images" ]] || { echo 'Image directory must exist and must not be a symbolic link.' >&2; exit 2; }
images="$(realpath -- "$images")"
[[ ! -e "$1" && ! -L "$1" ]] || { echo 'Backup destination already exists; refusing to overwrite it.' >&2; exit 2; }
destination="$(realpath -m -- "$1")"
case "$destination/" in "$images/"*) echo 'Backup destination cannot be inside the image source.' >&2; exit 2;; esac
mkdir -p -- "$(dirname -- "$destination")"
temporary="$(mktemp -d "$(dirname -- "$destination")/.pos-backup.XXXXXX")"
trap 'rm -rf -- "$temporary"' EXIT

run_pg pg_dump --format=custom --no-owner --no-acl "$target" > "$temporary/database.dump"
mkdir "$temporary/product-images"
cp -a -- "$images/." "$temporary/product-images/"
python3 "$script_directory/backup-manifest.py" create "$temporary"
python3 "$script_directory/backup-manifest.py" verify "$temporary"
mv -- "$temporary" "$destination"
echo "Verified database and image backup written to $destination"
