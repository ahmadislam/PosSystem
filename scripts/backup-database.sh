#!/usr/bin/env bash
set -euo pipefail
umask 077
script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source "$script_directory/postgres-tools.sh"

target="$(pg_target)"
if [[ $# -ne 1 ]]; then
  echo "Usage: DATABASE_URL=... $0 <backup-file.dump>" >&2
  exit 2
fi

backup_file="$1"
[[ ! -e "$backup_file" ]] || { echo 'Backup destination exists; refusing to overwrite.' >&2; exit 2; }
mkdir -p "$(dirname "$backup_file")"
run_pg pg_dump --format=custom --no-owner --no-acl "$target" > "$backup_file"
echo "Database backup written to $backup_file"
echo 'This dump does not include image files; use backup-state.sh for a complete recovery bundle.'
