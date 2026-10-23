# Integration tests

These tests run the real PostgreSQL schema/migrations, application services, and API process. They require a disposable PostgreSQL server with permission to create databases. Set `POS_TEST_POSTGRES` to its connection string; the tests create and drop only databases named `pos_it_<random UUID>`. They never reset the configured database or a developer database.

```bash
export POS_TEST_POSTGRES='Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=postgres'
dotnet test tests/PosSystem.IntegrationTests -c Release
```

The example matches the repository's local Docker database. Use secure environment settings for other servers. Do not put real credentials in committed files.

PostgreSQL cases explicitly skip when `POS_TEST_POSTGRES` is absent. CI additionally sets `POS_REQUIRE_POSTGRES_TESTS=true`, which makes an absent connection fail rather than skip. SMTP transport tests always run and do not require a provider account.

API tests launch the matching built API configuration in a child process using a unique loopback port. Startup, health, authentication/refresh/logout/revocation, HTTP checkout/returns, exact-SKU lookup, and enabled-email delivery are tested against a freshly migrated database. Processes and databases are removed in cleanup.

Financial tests cover deterministic concurrent shift closure, duplicate checkout/receiving, authoritative tender/change, repeated partial loyalty returns, penny-sized promotion allocation, and atomic audit rollback. Background tests cover durable alert retry/backoff/resolution and daily report totals.

The SMTP sink uses an ephemeral localhost certificate, test CA, and signed CRL. The isolated SMTP sender/API processes trust that CA only through their `SSL_CERT_FILE`; ordinary TLS hostname, chain, and revocation checks remain enabled. No certificate-store changes or permissive validation callbacks are used. Negative tests require an untrusted CA and a temporary SMTP failure to be rejected.

The suite does not validate a hosted SMTP provider, public deployment, production credentials, or third-party-cookie policies. Browser-level workflows are tested separately in `web/e2e`.
