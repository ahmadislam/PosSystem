# POS System - Point of Sale, Inventory & Sales Management

ASP.NET Core 10 Web API (Clean Architecture) + PostgreSQL, with an Angular point-of-sale frontend.

## Current capabilities
- JWT auth with refresh-token rotation (refresh token in an HttpOnly cookie, stored hashed)
- Roles: Admin, Manager, Cashier
- Categories, products (search, pagination), stock adjustments with a full stock movement history
- Cashier shifts: Open -> PendingClose -> Closed, with cash variance
- POS sales: transactional stock deduction, race-free invoice numbers, concurrency protection (xmin)
- Hangfire jobs stored in PostgreSQL: low-stock email alerts and a daily sales/inventory report (SMTP)
- Soft delete via EF Core global query filters, automatic audit trail (old vs new values)
- Angular POS, inventory, sales, shift, and dashboard screens; dashboard aggregates sales and low-stock metrics server-side
- Swagger UI, `/health`, global error handling, Docker, GitHub Actions CI/CD

## Run locally
Prerequisites: .NET 10 SDK, Docker (or any local PostgreSQL; edit `ConnectionStrings:Default`).

```bash
./setup.sh                      # one-time: creates projects, installs packages, builds, tests, adds migration
docker compose up -d db         # PostgreSQL
dotnet run --project src/PosSystem.Api --urls http://localhost:5080
```
Swagger: http://localhost:5080/swagger - default login `admin` / `Admin@12345` (change via `Seed__AdminPassword`).

### Email and background jobs
Email is disabled by default. Enable it with `Email__Enabled=true` and configure `Email__SmtpHost`,
`Email__SmtpPort`, `Email__SenderAddress`, and SMTP credentials (`Email__Username` and
`Email__Password`) through environment variables or your deployment's secret manager. Use
`Email__UseStartTls=true` for STARTTLS (for example, SMTP port 587), or set
`Email__UseSslOnConnect=true` for implicit TLS (commonly port 465).

Set the `ManagerEmail` value in the database `Settings` table to the report recipient. Low-stock
alerts also require `AlertsEnabled=true`. The daily report runs at midnight UTC and summarizes the
previous UTC day. Hangfire stores its queue and recurring-job state in PostgreSQL.

## Architecture
`Domain` (entities, pricing rules) <- `Application` (services, DTOs, abstractions) <- `Infrastructure` (EF Core, JWT) <- `Api` (controllers, composition root).

## Deploy (all free tiers)
- API: Render (Docker web service). Set `ConnectionStrings__Default`, `Jwt__Key`, `Cors__Origins__0`, `Seed__AdminPassword`, and SMTP settings if email jobs are enabled.
- Database: free external PostgreSQL (e.g. Supabase pooler connection string).
- CI/CD: add `RENDER_DEPLOY_HOOK` as a GitHub Actions secret.
# PosSystem
