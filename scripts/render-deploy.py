#!/usr/bin/env python3
"""Deploy the checked-out revision to Render and wait for it to become live."""
from __future__ import annotations

import json
import os
from pathlib import Path
import runpy
import time
import urllib.error
import urllib.request


diagnostics = runpy.run_path(str(Path(__file__).with_name("render-diagnostics.py")))
API = diagnostics["API"]
DEFAULT_URL = diagnostics["DEFAULT_URL"]
DiagnosticError = diagnostics["DiagnosticError"]
NoRedirect = diagnostics["NoRedirect"]
find_service = diagnostics["find_service"]
identifier = diagnostics["identifier"]
public_origin = diagnostics["public_origin"]
api_json = diagnostics["api_json"]


def create_deploy(service_id: str, token: str) -> str:
    url = f"{API}/services/{identifier(service_id, 'srv')}/deploys"
    request = urllib.request.Request(
        url,
        data=b"{}",
        method="POST",
        headers={
            "Authorization": "Bearer " + token,
            "Accept": "application/json",
            "Content-Type": "application/json",
            "User-Agent": "PosSystem-CI-deployer",
        },
    )
    try:
        with urllib.request.build_opener(NoRedirect()).open(request, timeout=45) as response:
            if response.status not in (200, 201, 202):
                raise DiagnosticError(f"Render rejected the deploy request with HTTP {response.status}.")
            body = response.read(1_000_001)
    except urllib.error.HTTPError as error:
        raise DiagnosticError(f"Render rejected the deploy request with HTTP {error.code}.") from None
    except (urllib.error.URLError, TimeoutError, OSError):
        raise DiagnosticError("The deploy request could not reach Render.") from None
    if len(body) > 1_000_000:
        raise DiagnosticError("Render returned an unexpectedly large deploy response.")
    try:
        payload = json.loads(body)
    except (ValueError, UnicodeError):
        raise DiagnosticError("Render returned an invalid deploy response.") from None
    deploy = payload.get("deploy", payload) if isinstance(payload, dict) else {}
    return identifier(deploy.get("id"), "dep")


def deployment(service_id: str, deploy_id: str, token: str) -> dict:
    deployments = api_json(f"/services/{service_id}/deploys", token, [("limit", "20")])
    if not isinstance(deployments, list):
        raise DiagnosticError("Render returned an unexpected deployments response.")
    for row in deployments:
        candidate = row.get("deploy", row) if isinstance(row, dict) else {}
        if candidate.get("id") == deploy_id:
            return candidate
    raise DiagnosticError("The requested deployment was not present in Render's recent deployment list.")


def main() -> int:
    token = os.environ.get("RENDER_API_KEY", "").strip()
    if not token:
        raise DiagnosticError("The RENDER_API_KEY GitHub Actions secret is missing.")
    origin = public_origin(os.environ.get("RENDER_PUBLIC_URL", DEFAULT_URL))
    service = find_service(token, origin, os.environ.get("RENDER_SERVICE_ID", "").strip())
    service_id = identifier(service.get("id"), "srv")
    deploy_id = create_deploy(service_id, token)
    print(f"Render accepted deployment {deploy_id} for {service_id}.")

    deadline = time.monotonic() + 15 * 60
    last_status = None
    failures = {"build_failed", "update_failed", "canceled", "deactivated"}
    while time.monotonic() < deadline:
        current = deployment(service_id, deploy_id, token)
        status = current.get("status")
        if status != last_status:
            print(f"Deployment status: {status}")
            last_status = status
        if status == "live":
            commit = (current.get("commit") or {}).get("id")
            expected = os.environ.get("GITHUB_SHA", "").strip()
            if expected and commit and commit != expected:
                raise DiagnosticError("Render deployed a different commit than the checked-out revision.")
            print("Render deployment is live.")
            return 0
        if status in failures:
            raise DiagnosticError(f"Render deployment ended with status {status}.")
        time.sleep(15)
    raise DiagnosticError("Timed out waiting for the Render deployment to become live.")


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except DiagnosticError as error:
        print("Render deploy: " + str(error), file=__import__("sys").stderr)
        raise SystemExit(1)

