# POS System - Inventory & Sales Management

ASP.NET Core 10 Web API (Clean Architecture) + PostgreSQL, with an Angular frontend (coming in phase 2).

## Phase 1 (this commit): backend foundation
- JWT auth with refresh-token rotation (refresh token in an HttpOnly cookie, stored hashed)
- Roles: Admin, Manager, Cashier
- Categories, products (search, pagination), stock adjustments with a full stock movement history
- Cashier shifts: Open -> PendingClose -> Closed, with cash variance
- POS sales: transactional stock deduction, race-free invoice numbers, concurrency protection (xmin)
- Soft delete via EF Core global query filters, automatic audit trail (old vs new values)
- Swagger UI, `/health`, global error handling, Docker, GitHub Actions CI/CD

## Run locally
Prerequisites: .NET 10 SDK, Docker (or any local PostgreSQL; edit `ConnectionStrings:Default`).

```bash
./setup.sh                      # one-time: creates projects, installs packages, builds, tests, adds migration
docker compose up -d db         # PostgreSQL
dotnet run --project src/PosSystem.Api --urls http://localhost:5080
```
Swagger: http://localhost:5080/swagger - default login `admin` / `Admin@12345` (change via `Seed__AdminPassword`).

## Architecture
`Domain` (entities, pricing rules) <- `Application` (services, DTOs, abstractions) <- `Infrastructure` (EF Core, JWT) <- `Api` (controllers, composition root).

## Deploy (all free tiers)
- API: Render (Docker web service). Set `ConnectionStrings__Default`, `Jwt__Key`, `Cors__Origins__0`, `Seed__AdminPassword`.
- Database: free external PostgreSQL (e.g. Supabase pooler connection string).
- CI/CD: add `RENDER_DEPLOY_HOOK` as a GitHub Actions secret.
# PosSystem
