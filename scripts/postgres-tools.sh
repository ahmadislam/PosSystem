#!/usr/bin/env bash
# Shared client runner. Sourced by the backup/restore scripts.

run_pg() {
  local command="$1"
  shift
  if [[ -n "${PG_DOCKER_CONTAINER:-}" ]]; then
    # Pass names, never interpolate secret values into command text or log them.
    local options=()
    local variable
    for variable in PGUSER PGPASSWORD PGHOST PGPORT PGDATABASE PGSSLMODE PGSSLROOTCERT; do
      if [[ -v "$variable" ]]; then options+=(--env "$variable"); fi
    done
    docker exec -i "${options[@]}" "$PG_DOCKER_CONTAINER" "$command" "$@"
  else
    "$command" "$@"
  fi
}

pg_target() {
  if [[ -n "${DATABASE_URL:-}" ]]; then
    printf '%s' "$DATABASE_URL"
  elif [[ -n "${PGDATABASE:-}" ]]; then
    printf '%s' "$PGDATABASE"
  else
    echo 'Set DATABASE_URL (libpq URI) or PGDATABASE and PostgreSQL connection variables.' >&2
    return 2
  fi
}
