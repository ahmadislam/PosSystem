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

The `deploy` job needs **all six** jobs to succeed. It only runs on a push to the configured deployment branch. Pull requests and pushes to other branches never receive deploy hooks. Deploy requests are serialized so two completed workflows cannot send hooks simultaneously.

## Connect the existing Render service

1. In the Render service settings, select the repository and deployment branch. The committed [blueprint](../render.yaml) and workflow default both use `feature/release-readiness`.
2. Disable Render Git auto-deploy so an unchecked commit cannot bypass GitHub Actions. The blueprint already sets `autoDeploy: false`; existing services must be checked in the dashboard.
3. Copy the service's deploy-hook URL into the GitHub repository Actions secret **`RENDER_DEPLOY_HOOK`**. Treat this URL as a credential; do not commit it or paste it into logs.
4. If deploying a different branch, set the GitHub repository Actions variable **`DEPLOY_BRANCH`** to that branch name and change the hosting service's branch to match. No variable is needed when using `feature/release-readiness`.
5. Push to that branch. Open the workflow summary, then Render **Events**, to confirm the tested change finishes deploying. A successful hook response only confirms the request was accepted, not that the hosting build has completed.

The combined Docker image serves the Angular frontend and API from one origin. A separate Vercel deployment is optional. If used, set **`VERCEL_DEPLOY_HOOK`** and align its target branch with `DEPLOY_BRANCH`, disable its independent Git auto-deploy, and configure `POS_API_ORIGIN` and the API's exact CORS origins as described in the [deployment guide](free-demo.md).

Missing deploy hooks are reported as **skipped** in the workflow summary. The repository configuration cannot set external repository secrets or confirm provider dashboard settings. No paid resources are provisioned by this workflow.

## Deployment checks

The existing public service address is [possystem-1-26mq.onrender.com](https://possystem-1-26mq.onrender.com). After Render reports a successful deployment, check:

- [Frontend login](https://possystem-1-26mq.onrender.com/login)
- [Database health](https://possystem-1-26mq.onrender.com/health), expected `Healthy`
- [Swagger UI](https://possystem-1-26mq.onrender.com/swagger/index.html)
- [OpenAPI document](https://possystem-1-26mq.onrender.com/swagger/v1/swagger.json)

During the 2026-10-23 repository audit, this execution environment's network proxy rejected access to the public host with `403 Forbidden`. These links therefore identify the existing target; they are **not an independent verification of its current health or deployed commit**. The database, SMTP secrets, and hosting dashboard credentials were not accessed or changed.

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
