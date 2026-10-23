import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import unittest
from unittest.mock import patch
import urllib.error

spec = importlib.util.spec_from_file_location(
    "render_diagnostics", Path(__file__).resolve().parents[2] / "scripts" / "render-diagnostics.py")
diagnostics = importlib.util.module_from_spec(spec)
spec.loader.exec_module(diagnostics)


class DiagnosticsTests(unittest.TestCase):
    def test_only_public_render_https_origins_are_allowed(self):
        self.assertEqual(diagnostics.DEFAULT_URL, diagnostics.public_origin(diagnostics.DEFAULT_URL + "/"))
        for url in ["http://localhost", "https://127.0.0.1", "https://example.com",
                    "https://user:password@app.onrender.com", "https://app.onrender.com/api",
                    "https://app.onrender.com?token=secret", "https://app.onrender.com:8443"]:
            with self.subTest(url=url), self.assertRaises(diagnostics.DiagnosticError):
                diagnostics.public_origin(url)

    def test_service_discovery_matches_exact_url_and_follows_cursor(self):
        selected = {"id": "srv-test123", "serviceDetails": {"url": diagnostics.DEFAULT_URL}}
        pages = [[{"service": {"id": "srv-wrong", "serviceDetails": {"url": "https://another.onrender.com"}}, "cursor": "next-page"}],
                 [{"service": selected}]]
        with patch.object(diagnostics, "api_json", side_effect=pages) as api:
            self.assertEqual(selected, diagnostics.find_service("private-key", diagnostics.DEFAULT_URL))
        self.assertIn(("cursor", "next-page"), api.call_args_list[1].args[2])

    def test_supplied_id_cannot_inspect_a_different_public_service(self):
        with patch.object(diagnostics, "api_json", return_value={"serviceDetails": {"url": "https://wrong.onrender.com"}}):
            with self.assertRaises(diagnostics.DiagnosticError):
                diagnostics.find_service("private-key", diagnostics.DEFAULT_URL, "srv-test123")

    def test_key_is_never_attached_to_a_non_render_api_request(self):
        with self.assertRaises(diagnostics.DiagnosticError):
            diagnostics.request_bytes("https://example.com/v1/services", "private-key")
        self.assertIsNone(diagnostics.NoRedirect().redirect_request(None, None, 302, "redirect", {}, "https://example.com"))

    def test_http_error_bodies_are_discarded(self):
        error = urllib.error.HTTPError(diagnostics.API + "/services", 401, "Unauthorized", {},
                                       io.BytesIO(b"private-key and a connection-string"))
        with patch.object(diagnostics.urllib.request, "build_opener") as opener:
            opener.return_value.open.side_effect = error
            status, body = diagnostics.request_bytes(diagnostics.API + "/services", "private-key")
        self.assertEqual((401, b""), (status, body))

    def test_logs_export_only_fixed_categories_never_sql_email_or_credentials(self):
        raw = "Unhandled exception. NpgsqlException: postgresql://user:private-key@host/db manager@example.test SELECT PasswordHash FROM Users"
        report = diagnostics.classify_logs({"logs": [{"message": raw, "timestamp": "private-key"}], "hasMore": True})
        rendered = json.dumps(report)
        self.assertEqual(1, report["diagnostic_counts"]["database_connection"])
        self.assertEqual(1, report["diagnostic_counts"]["unhandled_exception"])
        for private in ["private-key", "manager@example.test", "PasswordHash", "postgresql://"]:
            self.assertNotIn(private, rendered)
        self.assertTrue(report["more_entries_available"])

    def test_failed_probe_does_not_emit_response_body(self):
        with patch.object(diagnostics, "request_bytes", return_value=(500, b"database password=private-key")):
            result = diagnostics.probe(diagnostics.DEFAULT_URL, "/health")
        self.assertEqual({"path": "/health", "http_status": 500, "ok": False}, result)

    def test_unknown_log_response_is_not_reported_as_an_error_free_sample(self):
        with self.assertRaises(diagnostics.DiagnosticError):
            diagnostics.classify_logs({"unexpected": []})

    def test_service_report_omits_sensitive_render_metadata(self):
        service = {"id": "srv-test123", "ownerId": "tea-owner123", "branch": "feature/release-readiness",
                   "envVars": [{"value": "private-key"}], "dashboardUrl": "private-account-metadata"}
        deployment = [{"deploy": {"id": "dep-test123", "status": "live", "commit": {"id": "a" * 40, "message": "private-key"}}}]
        with patch.object(diagnostics, "find_service", return_value=service), \
             patch.object(diagnostics, "api_json", side_effect=[deployment, {"logs": []}]), \
             patch.object(diagnostics, "probe", return_value={"path": "/health", "ok": True}), \
             patch.dict(os.environ, {"GITHUB_SHA": "a" * 40}):
            report = diagnostics.collect("private-key", diagnostics.DEFAULT_URL)
        self.assertTrue(report["latest_deployment_matches_checked_commit"])
        self.assertNotIn("private-key", json.dumps(report))
        self.assertNotIn("private-account-metadata", json.dumps(report))

    def test_missing_secret_is_reported_without_a_network_request(self):
        stderr = io.StringIO()
        with patch.dict(os.environ, {}, clear=True), contextlib.redirect_stderr(stderr), \
             patch.object(diagnostics, "request_bytes") as request:
            self.assertEqual(1, diagnostics.main())
        request.assert_not_called()
        self.assertIn("secret is missing", stderr.getvalue())

    def test_missing_commit_or_log_owner_does_not_prevent_public_checks(self):
        service = {"id": "srv-test123", "ownerId": "unrecognized-owner"}
        with patch.object(diagnostics, "find_service", return_value=service), \
             patch.object(diagnostics, "api_json", return_value=[{"deploy": {"commit": None}}]), \
             patch.object(diagnostics, "probe", return_value={"ok": True}) as probe:
            report = diagnostics.collect("private-key", diagnostics.DEFAULT_URL)
        self.assertIsNone(report["latest_deployment"]["commit"])
        self.assertFalse(report["logs"]["available"])
        self.assertEqual(3, probe.call_count)


if __name__ == "__main__":
    unittest.main()
