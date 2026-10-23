import importlib.util
import io
import json
import os
import sys
import unittest
import urllib.error
from pathlib import Path
from unittest.mock import patch


SCRIPT = Path(__file__).parents[2] / "scripts" / "render-deploy.py"
SPEC = importlib.util.spec_from_file_location("render_deploy", SCRIPT)
render_deploy = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = render_deploy
SPEC.loader.exec_module(render_deploy)


SHA = "a" * 40
SERVICE_ID = "srv-test123"


class FakeResponse:
    def __init__(self, status, payload):
        self.status = status
        self.payload = payload

    def __enter__(self):
        return self

    def __exit__(self, *_args):
        return False

    def getcode(self):
        return self.status

    def read(self, _limit):
        if self.payload is None:
            return b""
        return json.dumps(self.payload).encode()


class CaptureOpener:
    def __init__(self, response=None, error=None):
        self.response = response
        self.error = error
        self.request = None

    def open(self, request, timeout):
        self.request = request
        if self.error:
            raise self.error
        return self.response


class DeployTests(unittest.TestCase):
    def test_service_must_match_repository_branch_and_disabled_autodeploy(self):
        def request(method, path, token, payload=None):
            self.assertEqual("GET", method)
            return 200, {
                "id": SERVICE_ID,
                "repo": "https://github.com/ahmadislam/PosSystem",
                "branch": "feature/release-readiness",
                "autoDeploy": "no",
                "autoDeployTrigger": "off",
            }

        render_deploy.verify_service(
            "token",
            SERVICE_ID,
            "https://github.com/ahmadislam/PosSystem",
            "feature/release-readiness",
            request=request,
        )

        with self.assertRaisesRegex(render_deploy.DeploymentError, "different branch"):
            render_deploy.verify_service(
                "token",
                SERVICE_ID,
                "https://github.com/ahmadislam/PosSystem",
                "main",
                request=request,
            )

    def test_trigger_uses_fixed_origin_exact_commit_and_no_redirects(self):
        opener = CaptureOpener(FakeResponse(201, {"id": "dep-test123", "status": "created"}))
        status, _ = render_deploy.api_json(
            "POST",
            f"/v1/services/{SERVICE_ID}/deploys",
            "secret-value",
            {"commitId": SHA, "clearCache": "do_not_clear"},
            opener=opener,
        )
        self.assertEqual(201, status)
        self.assertEqual("api.render.com", opener.request.host)
        self.assertEqual("POST", opener.request.method)
        self.assertEqual("Bearer secret-value", opener.request.get_header("Authorization"))
        self.assertEqual(
            {"commitId": SHA, "clearCache": "do_not_clear"},
            json.loads(opener.request.data),
        )
        self.assertIsNone(render_deploy.NoRedirect().redirect_request(None, None, 302, "", {}, "https://example.com"))

    def test_api_error_body_is_never_in_exception(self):
        error = urllib.error.HTTPError(
            "https://api.render.com/v1/services/x/deploys",
            403,
            "Forbidden",
            {},
            io.BytesIO(b"database password and customer@example.com"),
        )
        opener = CaptureOpener(error=error)
        with self.assertRaises(render_deploy.DeploymentError) as raised:
            render_deploy.api_json("GET", "/v1/services/srv-x/deploys", "secret", opener=opener)
        self.assertEqual("Render API request failed with HTTP 403.", str(raised.exception))

    def test_deploy_waits_for_exact_commit_to_be_live(self):
        calls = []

        def request(method, path, token, payload=None):
            calls.append((method, path, token, payload))
            if method == "POST":
                return 201, {"id": "dep-test123", "status": "created", "commit": {"id": SHA}}
            if len(calls) == 2:
                return 200, {"id": "dep-test123", "status": "build_in_progress", "commit": {"id": SHA}}
            return 200, {"id": "dep-test123", "status": "live", "commit": {"id": SHA}}

        ticks = iter([0, 0, 1, 2])
        result = render_deploy.deploy_commit(
            "token",
            SERVICE_ID,
            SHA,
            timeout_seconds=30,
            poll_seconds=1,
            request=request,
            sleep=lambda _: None,
            monotonic=lambda: next(ticks),
        )
        self.assertEqual("live", result["status"])
        self.assertEqual(SHA, calls[0][3]["commitId"])

    def test_accepted_deploy_is_discovered_by_commit(self):
        calls = []

        def request(method, path, token, payload=None):
            calls.append((method, path))
            if method == "POST":
                return 202, None
            return 200, [{"deploy": {"id": "dep-later123", "status": "live", "commit": {"id": SHA}}}]

        result = render_deploy.deploy_commit(
            "token",
            SERVICE_ID,
            SHA,
            timeout_seconds=30,
            poll_seconds=1,
            request=request,
            sleep=lambda _: None,
            monotonic=lambda: 0,
        )
        self.assertEqual("dep-later123", result["id"])
        self.assertIn("?limit=20", calls[1][1])

    def test_terminal_failure_fails_the_job(self):
        def request(method, path, token, payload=None):
            if method == "POST":
                return 201, {"id": "dep-test123", "status": "created", "commit": {"id": SHA}}
            return 200, {"id": "dep-test123", "status": "build_failed", "commit": {"id": SHA}}

        with self.assertRaisesRegex(render_deploy.DeploymentError, "build_failed"):
            render_deploy.deploy_commit(
                "token",
                SERVICE_ID,
                SHA,
                timeout_seconds=30,
                poll_seconds=1,
                request=request,
                sleep=lambda _: None,
                monotonic=lambda: 0,
            )

    def test_main_rejects_missing_secret_without_network(self):
        with patch.dict(
            os.environ,
            {
                "RENDER_SERVICE_ID": SERVICE_ID,
                "RENDER_COMMIT_SHA": SHA,
                "RENDER_EXPECTED_REPOSITORY": "https://github.com/ahmadislam/PosSystem",
                "RENDER_EXPECTED_BRANCH": "feature/release-readiness",
            },
            clear=True,
        ), patch.object(render_deploy, "api_json") as request:
            self.assertEqual(1, render_deploy.main())
            request.assert_not_called()


if __name__ == "__main__":
    unittest.main()
