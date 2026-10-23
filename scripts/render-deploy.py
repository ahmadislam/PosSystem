#!/usr/bin/env python3
"""Deploy the exact CI-tested commit to one Render service and wait for it.

The API key is sent only to Render's fixed API origin. Redirects and API error
bodies are deliberately ignored so credentials and provider details cannot be
copied into GitHub Actions output.
"""

from __future__ import annotations

import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any, Callable


API_ORIGIN = "https://api.render.com"
MAX_RESPONSE_BYTES = 2 * 1024 * 1024
SERVICE_ID_PATTERN = re.compile(r"^srv-[A-Za-z0-9]+$")
DEPLOY_ID_PATTERN = re.compile(r"^dep-[A-Za-z0-9]+$")
COMMIT_PATTERN = re.compile(r"^[0-9a-fA-F]{40}$")
BRANCH_PATTERN = re.compile(r"^[A-Za-z0-9._/-]+$")
FAILED_STATES = {"build_failed", "pre_deploy_failed", "update_failed", "canceled", "deactivated"}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):  # noqa: ANN001
        return None


class DeploymentError(RuntimeError):
    pass


def validate_service_id(value: str) -> str:
    if not SERVICE_ID_PATTERN.fullmatch(value):
        raise DeploymentError("RENDER_SERVICE_ID must be a Render srv-* identifier.")
    return value


def validate_commit(value: str) -> str:
    if not COMMIT_PATTERN.fullmatch(value):
        raise DeploymentError("RENDER_COMMIT_SHA must be a full 40-character Git SHA.")
    return value.lower()


def _safe_api_url(path: str) -> str:
    if not path.startswith("/v1/"):
        raise DeploymentError("Render API path is invalid.")
    url = API_ORIGIN + path
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != "https" or parsed.netloc != "api.render.com":
        raise DeploymentError("Render API destination is invalid.")
    return url


def api_json(
    method: str,
    path: str,
    token: str,
    payload: dict[str, Any] | None = None,
    *,
    opener=None,
) -> tuple[int, Any]:
    if not token:
        raise DeploymentError("RENDER_API_KEY is not configured.")

    body = None if payload is None else json.dumps(payload, separators=(",", ":")).encode("utf-8")
    headers = {"Authorization": f"Bearer {token}", "Accept": "application/json"}
    if body is not None:
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(_safe_api_url(path), data=body, headers=headers, method=method)
    client = opener or urllib.request.build_opener(NoRedirect())

    try:
        with client.open(request, timeout=45) as response:
            status = response.getcode()
            raw = response.read(MAX_RESPONSE_BYTES + 1)
    except urllib.error.HTTPError as error:
        # Do not read or surface Render's response body; it can contain account details.
        raise DeploymentError(f"Render API request failed with HTTP {error.code}.") from None
    except (OSError, urllib.error.URLError, TimeoutError):
        raise DeploymentError("Render API request could not reach its destination.") from None

    if len(raw) > MAX_RESPONSE_BYTES:
        raise DeploymentError("Render API response exceeded the allowed size.")
    if not raw:
        return status, None
    try:
        return status, json.loads(raw)
    except (UnicodeDecodeError, json.JSONDecodeError):
        raise DeploymentError("Render API returned an invalid JSON response.") from None


def _commit_id(deploy: dict[str, Any]) -> str | None:
    commit = deploy.get("commit")
    if not isinstance(commit, dict):
        return None
    value = commit.get("id")
    return value.lower() if isinstance(value, str) else None


def _deploy_id(deploy: dict[str, Any]) -> str | None:
    value = deploy.get("id")
    return value if isinstance(value, str) and DEPLOY_ID_PATTERN.fullmatch(value) else None


def verify_service(
    token: str,
    service_id: str,
    expected_repository: str,
    expected_branch: str,
    *,
    request: Callable[..., tuple[int, Any]] = api_json,
) -> None:
    service_id = validate_service_id(service_id)
    repository = urllib.parse.urlsplit(expected_repository)
    if (
        repository.scheme != "https"
        or repository.netloc != "github.com"
        or repository.username is not None
        or repository.password is not None
        or repository.query
        or repository.fragment
        or len([part for part in repository.path.split("/") if part]) != 2
    ):
        raise DeploymentError("RENDER_EXPECTED_REPOSITORY must be an HTTPS GitHub repository URL.")
    if not BRANCH_PATTERN.fullmatch(expected_branch) or ".." in expected_branch:
        raise DeploymentError("RENDER_EXPECTED_BRANCH is invalid.")

    status, service = request("GET", f"/v1/services/{service_id}", token)
    if status != 200 or not isinstance(service, dict):
        raise DeploymentError("Render service response was invalid.")
    actual_repo = service.get("repo")
    if isinstance(actual_repo, str):
        actual_repo = actual_repo.removesuffix(".git")
    if actual_repo != expected_repository.removesuffix(".git"):
        raise DeploymentError("Render service is connected to a different repository.")
    if service.get("branch") != expected_branch:
        raise DeploymentError("Render service is connected to a different branch.")
    if service.get("autoDeploy") != "no" or service.get("autoDeployTrigger") != "off":
        raise DeploymentError("Disable Render Git auto-deploy before using the CI deployment gate.")


def _find_commit_deploy(payload: Any, commit_sha: str) -> dict[str, Any] | None:
    if not isinstance(payload, list):
        raise DeploymentError("Render deploy list had an unexpected shape.")
    for item in payload:
        if not isinstance(item, dict):
            continue
        deploy = item.get("deploy")
        if isinstance(deploy, dict) and _commit_id(deploy) == commit_sha:
            return deploy
    return None


def deploy_commit(
    token: str,
    service_id: str,
    commit_sha: str,
    *,
    timeout_seconds: int = 900,
    poll_seconds: int = 10,
    request: Callable[..., tuple[int, Any]] = api_json,
    sleep: Callable[[float], None] = time.sleep,
    monotonic: Callable[[], float] = time.monotonic,
) -> dict[str, Any]:
    service_id = validate_service_id(service_id)
    commit_sha = validate_commit(commit_sha)
    if timeout_seconds < 30 or timeout_seconds > 1800:
        raise DeploymentError("Deployment timeout must be between 30 and 1800 seconds.")
    if poll_seconds < 1 or poll_seconds > 60:
        raise DeploymentError("Deployment polling interval must be between 1 and 60 seconds.")

    status, created = request(
        "POST",
        f"/v1/services/{service_id}/deploys",
        token,
        {"commitId": commit_sha, "clearCache": "do_not_clear"},
    )
    if status not in {201, 202}:
        raise DeploymentError(f"Render deploy request returned unexpected HTTP {status}.")

    deploy_id = _deploy_id(created) if isinstance(created, dict) else None
    if status == 201 and deploy_id is None:
        raise DeploymentError("Render accepted the deployment without returning a deploy ID.")
    if isinstance(created, dict) and _commit_id(created) not in {None, commit_sha}:
        raise DeploymentError("Render created a deployment for a different commit.")

    deadline = monotonic() + timeout_seconds
    latest = created if isinstance(created, dict) else None
    while monotonic() <= deadline:
        if deploy_id:
            current_status, current = request(
                "GET", f"/v1/services/{service_id}/deploys/{deploy_id}", token
            )
            if current_status != 200 or not isinstance(current, dict):
                raise DeploymentError("Render deploy status response was invalid.")
            latest = current
        else:
            list_status, deployments = request(
                "GET", f"/v1/services/{service_id}/deploys?limit=20", token
            )
            if list_status != 200:
                raise DeploymentError("Render deploy list response was invalid.")
            latest = _find_commit_deploy(deployments, commit_sha)
            if latest is not None:
                deploy_id = _deploy_id(latest)
                if deploy_id is None:
                    raise DeploymentError("Render returned an invalid deploy ID.")

        if latest is not None:
            state = latest.get("status")
            actual_commit = _commit_id(latest)
            if actual_commit not in {None, commit_sha}:
                raise DeploymentError("Render deploy status referred to a different commit.")
            if state == "live":
                if actual_commit != commit_sha:
                    raise DeploymentError("Render marked a deployment live without the expected commit.")
                return latest
            if state in FAILED_STATES:
                raise DeploymentError(f"Render deployment ended in terminal state: {state}.")

        sleep(poll_seconds)

    raise DeploymentError("Timed out waiting for the Render deployment to become live.")


def _positive_int(name: str, default: int) -> int:
    raw = os.environ.get(name, str(default))
    try:
        return int(raw)
    except ValueError:
        raise DeploymentError(f"{name} must be an integer.") from None


def main() -> int:
    try:
        token = os.environ.get("RENDER_API_KEY", "")
        service_id = os.environ.get("RENDER_SERVICE_ID", "")
        commit_sha = os.environ.get("RENDER_COMMIT_SHA") or os.environ.get("GITHUB_SHA", "")
        verify_service(
            token,
            service_id,
            os.environ.get("RENDER_EXPECTED_REPOSITORY", ""),
            os.environ.get("RENDER_EXPECTED_BRANCH", ""),
        )
        result = deploy_commit(
            token,
            service_id,
            commit_sha,
            timeout_seconds=_positive_int("RENDER_DEPLOY_TIMEOUT_SECONDS", 900),
            poll_seconds=_positive_int("RENDER_DEPLOY_POLL_SECONDS", 10),
        )
        deploy_id = _deploy_id(result)
        message = f"Render deployment {deploy_id} is live for commit {commit_sha[:7]}."
        print(message)
        summary = os.environ.get("GITHUB_STEP_SUMMARY")
        if summary:
            with Path(summary).open("a", encoding="utf-8") as stream:
                stream.write(f"Render: exact tested commit `{commit_sha}` is live ({deploy_id}).\n")
        return 0
    except DeploymentError as error:
        print(f"Render deployment failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
