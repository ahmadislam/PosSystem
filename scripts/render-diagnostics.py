#!/usr/bin/env python3
"""Read-only Render checks. Never persist credentials, response bodies, or raw log messages."""
from __future__ import annotations

import datetime as dt
import json
import os
from pathlib import Path
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

API = "https://api.render.com/v1"
DEFAULT_URL = "https://possystem-1-26mq.onrender.com"
ERROR_SIGNATURES = {
    "unhandled_exception": r"Unhandled exception",
    "database_connection": r"NpgsqlException|Npgsql\.NpgsqlException|Failed to connect to|connection refused",
    "database_timeout": r"Npgsql.*timeout|Timeout during reading attempt",
    "database_migration": r"PendingModelChangesWarning|migration.*failed|Failed executing DbCommand",
    "missing_seed_password": r"Set Seed:AdminPassword",
    "invalid_connection_string": r"Couldn't set postgresql|Format of the initialization string",
    "smtp_delivery": r"SMTP delivery failed|SmtpCommandException|SmtpProtocolException",
    "smtp_authentication": r"AuthenticationException|535 .*authentication",
    "background_job_failure": r"Failed to process the job|Background job processing failed",
    "authorization_failure": r"Authorization failed|Bearer was not authenticated",
    "http_5xx": r"Request finished.*\s5\d\d\s",
}


class DiagnosticError(Exception):
    """Only fixed, safe operational descriptions are passed to this exception."""


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def public_origin(value: str) -> str:
    parsed = urllib.parse.urlsplit(value)
    if (parsed.scheme != "https" or not parsed.hostname
            or not parsed.hostname.endswith(".onrender.com")
            or parsed.username or parsed.password or parsed.port
            or parsed.path not in ("", "/") or parsed.query or parsed.fragment):
        raise DiagnosticError("RENDER_PUBLIC_URL must be an HTTPS onrender.com origin without credentials or a path.")
    return f"https://{parsed.hostname}"


def identifier(value: object, prefix: str) -> str:
    if not isinstance(value, str) or not re.fullmatch(prefix + r"-[a-zA-Z0-9]+", value):
        raise DiagnosticError("Render returned an unexpected identifier format.")
    return value


def safe_label(value: object) -> str | None:
    return value if isinstance(value, str) and re.fullmatch(r"[A-Za-z0-9_./-]{1,100}", value) else None


def safe_time(value: object) -> str | None:
    if not isinstance(value, str):
        return None
    try:
        return dt.datetime.fromisoformat(value.replace("Z", "+00:00")).isoformat()
    except ValueError:
        return None


def request_bytes(url: str, token: str | None = None, limit: int = 4_000_000) -> tuple[int, bytes]:
    headers = {"User-Agent": "PosSystem-read-only-diagnostics", "Accept": "application/json"}
    if token:
        # Refuse sending the API key to any origin other than the Render API, including redirects.
        if not url.startswith(API + "/"):
            raise DiagnosticError("Refusing to send a Render credential to a different destination.")
        headers["Authorization"] = "Bearer " + token
    opener = urllib.request.build_opener(NoRedirect())
    try:
        with opener.open(urllib.request.Request(url, headers=headers), timeout=45) as response:
            data = response.read(limit + 1)
            if len(data) > limit:
                raise DiagnosticError("A diagnostics response exceeded the safe size limit.")
            return response.status, data
    except urllib.error.HTTPError as error:
        return error.code, b""  # API error bodies and Location headers can contain private data.
    except (urllib.error.URLError, TimeoutError, OSError):
        raise DiagnosticError("A diagnostics request could not reach its destination.") from None


def api_json(path: str, token: str, params: list[tuple[str, str]] | None = None):
    url = API + path
    if params:
        url += "?" + urllib.parse.urlencode(params)
    status, body = request_bytes(url, token)
    if status != 200:
        raise DiagnosticError(f"Render API request failed with HTTP {status}; check API-key access and service permissions.")
    try:
        return json.loads(body)
    except (ValueError, UnicodeError):
        raise DiagnosticError("Render returned an invalid JSON response.") from None


def find_service(token: str, origin: str, service_id: str = "") -> dict:
    if service_id:
        result = api_json("/services/" + identifier(service_id, "srv"), token)
        service = result.get("service", result) if isinstance(result, dict) else {}
        if service.get("serviceDetails", {}).get("url", "").rstrip("/") != origin:
            raise DiagnosticError("The supplied service ID does not match RENDER_PUBLIC_URL.")
        return service
    params = [("limit", "100")]
    seen = set()
    for _ in range(10):
        page = api_json("/services", token, params)
        if not isinstance(page, list):
            raise DiagnosticError("Render returned an unexpected services response.")
        matches = [row["service"] for row in page if isinstance(row, dict)
                   and isinstance(row.get("service"), dict)
                   and row["service"].get("serviceDetails", {}).get("url", "").rstrip("/") == origin]
        if len(matches) > 1:
            raise DiagnosticError("More than one service matched the public URL; set RENDER_SERVICE_ID.")
        if matches:
            return matches[0]
        cursor = page[-1].get("cursor") if page and isinstance(page[-1], dict) else None
        if not isinstance(cursor, str) or not cursor or cursor in seen:
            break
        seen.add(cursor)
        params = [("limit", "100"), ("cursor", cursor)]
    raise DiagnosticError("No accessible Render service matches RENDER_PUBLIC_URL; check the key's workspace access or set RENDER_SERVICE_ID.")


def classify_logs(payload: dict) -> dict:
    logs = payload.get("logs")
    if not isinstance(logs, list):
        raise DiagnosticError("Render returned an unexpected logs response.")
    counts = {name: 0 for name in ERROR_SIGNATURES}
    for entry in logs:
        message = entry.get("message", "") if isinstance(entry, dict) else ""
        if not isinstance(message, str):
            continue
        for name, pattern in ERROR_SIGNATURES.items():
            if re.search(pattern, message, re.I):
                counts[name] += 1
    return {"available": True, "entries_scanned": len(logs),
            "more_entries_available": bool(payload.get("hasMore")),
            "diagnostic_counts": counts,
            "note": "Counts cover a bounded recent sample. Raw messages, SQL, addresses and exception details are never exported."}


def probe(origin: str, path: str) -> dict:
    try:
        status, body = request_bytes(origin + path)
        if path == "/health":
            ok = status == 200 and body.strip() == b"Healthy"
        elif path == "/login":
            ok = status == 200 and b"<app-root" in body
        else:
            try:
                schema = json.loads(body)
                ok = status == 200 and isinstance(schema, dict) and bool(schema.get("paths"))
            except (ValueError, UnicodeError):
                ok = False
        return {"path": path, "http_status": status, "ok": ok}
    except DiagnosticError:
        return {"path": path, "http_status": None, "ok": False}


def collect(token: str, origin: str, service_id: str = "") -> dict:
    service = find_service(token, origin, service_id)
    sid = identifier(service.get("id"), "srv")
    report = {"checked_at": dt.datetime.now(dt.timezone.utc).isoformat(),
              "service": {"id": sid, "url": origin, "branch": safe_label(service.get("branch"))},
              "expected_commit": safe_label(os.environ.get("GITHUB_SHA"))}
    deployments = api_json(f"/services/{sid}/deploys", token, [("limit", "5")])
    if not isinstance(deployments, list):
        raise DiagnosticError("Render returned an unexpected deployments response.")
    deploy = (deployments[0].get("deploy") or {}) if deployments else {}
    commit = deploy.get("commit") or {}
    report["latest_deployment"] = {"id": safe_label(deploy.get("id")), "status": safe_label(deploy.get("status")),
                                   "commit": safe_label(commit.get("id")),
                                   "created_at": safe_time(deploy.get("createdAt")), "finished_at": safe_time(deploy.get("finishedAt"))}
    report["latest_deployment_matches_checked_commit"] = (
        report["latest_deployment"]["commit"] == report["expected_commit"]
        if report["latest_deployment"]["commit"] and report["expected_commit"] else None)
    owner = service.get("ownerId")
    if owner is None:
        report["logs"] = {"available": False, "reason": "Render did not return the required ownerId."}
    else:
        start = (dt.datetime.now(dt.timezone.utc) - dt.timedelta(hours=2)).isoformat()
        try:
            owner = identifier(owner, "(?:tea|usr)")
            logs = api_json("/logs", token, [("ownerId", owner), ("resource", sid),
                                             ("startTime", start), ("direction", "backward"), ("limit", "100")])
            if not isinstance(logs, dict):
                raise DiagnosticError("Render returned an unexpected logs response.")
            report["logs"] = classify_logs(logs)
        except DiagnosticError as error:
            report["logs"] = {"available": False, "reason": str(error)}
    report["probes"] = [probe(origin, path) for path in ("/health", "/login", "/swagger/v1/swagger.json")]
    return report


def main() -> int:
    # Do not echo environment contents, API errors, or exception traces from this process.
    token = os.environ.get("RENDER_API_KEY", "").strip()
    try:
        if not token:
            raise DiagnosticError("The RENDER_API_KEY GitHub Actions secret is missing or unavailable to this run.")
        origin = public_origin(os.environ.get("RENDER_PUBLIC_URL", DEFAULT_URL))
        report = collect(token, origin, os.environ.get("RENDER_SERVICE_ID", "").strip())
        output = Path(os.environ.get("RENDER_DIAGNOSTICS_OUTPUT", "render-diagnostics.json"))
        output.write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps(report, indent=2))
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            Path(os.environ["GITHUB_STEP_SUMMARY"]).write_text(
                "## Render diagnostics (read-only)\n\n```json\n" + json.dumps(report, indent=2) + "\n```\n")
        return 0 if all(p["ok"] for p in report["probes"]) else 1
    except DiagnosticError as error:
        print("Render diagnostics: " + str(error), file=sys.stderr)
        return 1
    except Exception:
        print("Render diagnostics could not complete; no raw response or credential data was exported.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
