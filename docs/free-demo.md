# Free portfolio demo: Render + Neon

Use one Render **Free web service** for the Angular frontend and .NET API, and one Neon
**Free PostgreSQL project**. No separate frontend host, paid disk, or SMTP account is needed.
Check the current free-plan quotas and signup requirements in each dashboard; do not select
a paid plan or add billing features for this demo.

## 1. Create the database

Create a free account at https://neon.tech and a free PostgreSQL project near your Render region.
Keep the connection details private. Use the **direct** database hostname, not a `-pooler`
hostname, so Hangfire can use PostgreSQL session locks normally.

Render expects a .NET/Npgsql connection string, not a `postgresql://` URI. Assemble it from
Neon's connection details, replacing every placeholder in Render's secure environment editor:

```text
Host=YOUR_DIRECT_NEON_HOST;Database=YOUR_DATABASE;Username=YOUR_USER;Password=YOUR_PASSWORD;SSL Mode=VerifyFull;Maximum Pool Size=10;Timeout=30
```

Do not paste real credentials into chat, source code, or shell history. Quote any password
containing connection-string separators according to Npgsql connection-string rules.

## 2. Create the web service

1. Create a free account at https://render.com using your GitHub account.
2. Choose **New → Blueprint** and connect `ahmadislam/PosSystem`.
3. Select branch `feature/release-readiness`, which contains `render.yaml` and the complete Docker image.
4. Confirm the service plan is **Free**, with no paid disk or database add-on.
5. Set the requested `ConnectionStrings__Default` value securely using the string above.
6. Deploy. Render generates private `Jwt__Key` and `Seed__AdminPassword` values automatically.

The Docker build compiles Angular and publishes the API. The API serves both at the Render
HTTPS URL. Angular uses its own browser origin for `/api` calls, so refresh cookies stay on
the same site. Render's `RENDER_EXTERNAL_URL` supplies the allowed public origin automatically;
on another host, set `Web__PublicOrigin` to the exact public HTTPS origin.

The blueprint uses `ProductImages__Storage=Database`, storing image bytes separately in
PostgreSQL rather than Render's temporary filesystem. Uploaded images therefore survive
service restarts. Image bytes count against the database quota; keep demo images small.
Existing file-mode deployments keep their original file storage; this does not move old files.

## 3. Sign in and check the demo

Open the service URL, sign in as `admin` using `Seed__AdminPassword` from Render's secure
environment settings, and create sample categories/products. Do not publish the admin password.
Create a limited Cashier account if you want to share access with someone.

Verify `/health` returns `Healthy`, then open a shift, record a sample sale, download its PDF,
return an item, and close the shift. Upload a small product image and confirm it remains after
a manual service restart. Only use synthetic demo records.

## Free-tier behavior and updates

Free web services can sleep while idle and take time to wake up. Hangfire jobs cannot execute
while the service is asleep, so the demo disables email. This configuration is for portfolio
demonstrations rather than daily shop operation. Free quotas and provider policies can change.

Automatic deployment is disabled in the blueprint. GitHub Actions validates the branch;
use Render's manual deploy after the checks pass, or configure `RENDER_DEPLOY_HOOK` for gated CD.
The workflow defaults to `feature/release-readiness`, matching this blueprint; `DEPLOY_BRANCH`
can select another deployment branch. Keep the provider branch and repository variable aligned
and provider Git auto-deploy disabled. See [CI/CD setup](ci-cd.md).

Database-mode images are included in database dumps. The existing database-and-image bundle
scripts still support file-mode deployments. Export backups before changing or deleting projects.

## Local combined-container check

```bash
docker compose --profile full up --build
```

The complete UI and API are served at port 8080 with the existing local Development seed.
Local same-origin loopback HTTP is supported; public production origins require HTTPS.
For a Render-like local run, use the Docker image with `ASPNETCORE_ENVIRONMENT=Production`,
private strong seed/JWT settings, `Web__PublicOrigin`, and a disposable PostgreSQL database.

Provider account creation, database credentials, and an actual public deployment are external
steps. Preparing the repository or passing local tests does not establish a live hosted URL.
