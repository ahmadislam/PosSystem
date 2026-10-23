# Continuous integration and deployment

The [CI-CD workflow](../.github/workflows/ci-cd.yml) runs on every push and on pull requests targeting `main`. It preserves six independent readiness checks:

| Job | Evidence |
| --- | --- |
| `api` | Restore and build .NET 10; execute the xUnit unit tests. |
| `web` | Install the lockfile dependencies; execute Angular/Vitest tests and the production build. |
| `integration` | Execute API, authentication, finance, jobs, and SMTP tests against disposable PostgreSQL 16 databases; retain TRX results. |
| `e2e` | Start a disposable API/frontend and exercise Chromium workflows with Playwright; retain browser traces on failure. |
| `backup-restore` | Verify a database plus image backup, restore it, and reject invalid restore targets. |
| `container` | Build the combined Angular/API Docker image; verify its health endpoint, SPA route, and runtime configuration. |

The `deploy` job needs **all six** jobs to succeed. It only runs on a push to the configured deployment branch. Pull requests and pushes to other branches never run a production deployment. Deploy requests are serialized so two completed workflows cannot deploy simultaneously.

## Connect the existing Render service

1. In the Render service settings, select the repository and deployment branch. The committed [blueprint](../render.yaml) and workflow default both use `feature/release-readiness`.
2. Disable Render Git auto-deploy so an unchecked commit cannot bypass GitHub Actions. The blueprint already sets `autoDeploy: false`; existing services must be checked in the dashboard.
3. Create a Render account API key and save it as the GitHub repository Actions secret
   **`RENDER_API_KEY`**. It is sent only to the fixed Render API origin; redirects and provider error
   bodies are refused. Treat it as a credential and rotate it if access changes.
4. The workflow defaults to the current demo service. Set the optional GitHub Actions variable
   **`RENDER_SERVICE_ID`** when deploying another `srv-*` service.
5. If deploying a different branch, set the GitHub repository Actions variable **`DEPLOY_BRANCH`** to that branch name and change the hosting service's branch to match. No variable is needed when using `feature/release-readiness`.
6. Push to that branch. The deploy job requests the exact tested commit, polls Render, and succeeds
   only after Render reports that commit `live`.

The combined Docker image serves the Angular frontend and API from one origin. A separate Vercel deployment is optional. If used, set **`VERCEL_DEPLOY_HOOK`** and align its target branch with `DEPLOY_BRANCH`, disable its independent Git auto-deploy, and configure `POS_API_ORIGIN` and the API's exact CORS origins as described in the [deployment guide](free-demo.md).

A missing Render API key fails the deployment job. A missing optional Vercel hook is reported as
**skipped**. No paid resources are provisioned by this workflow.

## Deployment checks

The existing public service address is [possystem-1-26mq.onrender.com](https://possystem-1-26mq.onrender.com). After Render reports a successful deployment, check:

- [Frontend login](https://possystem-1-26mq.onrender.com/login)
- [Database health](https://possystem-1-26mq.onrender.com/health), expected `Healthy`
- [Swagger UI](https://possystem-1-26mq.onrender.com/swagger/index.html)
- [OpenAPI document](https://possystem-1-26mq.onrender.com/swagger/v1/swagger.json)

Live verification completed on 2026-10-23. The Render API reported `PosSystem-1` on
`feature/release-readiness`, Git auto-deploy disabled, and the audited deployment in `live` state.
Independent probes returned HTTP 200 for `/health`, `/login`, and
`/swagger/v1/swagger.json`. The matching [GitHub push run](https://github.com/ahmadislam/PosSystem/actions/runs/40037957626)
passed all six readiness jobs before its deploy job succeeded. Render log retrieval returned 403
for the read-only diagnostic key; this did not prevent deployment metadata or public endpoint
verification.

## Read-only Render diagnostics

The [Render diagnostics workflow](../.github/workflows/render-diagnostics.yml) uses the GitHub
Actions repository secret `RENDER_API_KEY`. Create a temporary Render account API key and save
it under that name in **GitHub repository → Settings → Secrets and variables → Actions**.
The key is used inside the Actions runner and is never returned to chat or written to artifacts.
Revoke it when diagnostics are finished.

The workflow finds the service whose public URL exactly matches
`https://possystem-1-26mq.onrender.com`. No service ID is normally needed. Optional repository
variables are `RENDER_SERVICE_ID` and `RENDER_PUBLIC_URL`; the latter must be an HTTPS
`onrender.com` origin, and both must refer to the same service.

On the release branch, changes to the diagnostics script/workflow trigger a run automatically.
Open [Render diagnostics runs](https://github.com/ahmadislam/PosSystem/actions?query=workflow%3A%22Render%20diagnostics%22)
and inspect the job summary or download the `render-diagnostics` artifact. Later checks can use
**Re-run all jobs**. `workflow_dispatch` is also declared, but GitHub displays its manual run
button only when that workflow exists on the default branch.

The report includes the latest deployment status/commit, whether that commit matches the checked
revision, and public health, Angular login and OpenAPI checks. It also counts recognized error
categories from up to 100 recent log entries in a two-hour window when Render grants log access.
Raw logs, SQL statements, customer addresses, environment variables, connection strings and API
error bodies are never exported. Missing log permission is reported separately; an empty sample
does not establish that older errors never occurred. A deployment can still be building when
these checks run, so a revision mismatch should be compared with Render Events.

This workflow does not deploy, restart, suspend, change configuration, or modify application data.
It requires only read permissions from GitHub. Render account API keys may have broader account
capabilities; the script uses only GET requests and refuses credential-bearing redirects.

Run the safeguards locally without any credentials or network access:

```bash
python3 -m unittest discover -s tests/render-diagnostics -v
```

A secret stored in GitHub does not automatically become an agent-environment credential. To
read GitHub Actions results from an agent, its network policy must permit `api.github.com` and
its GitHub authentication must authorize the repository API. Render API access additionally
requires `api.render.com` and a separately configured secure environment binding if direct
access is desired. Public verification requires the application's hostname. Environment draft
changes must be reviewed/saved/published before treating that access as active.
