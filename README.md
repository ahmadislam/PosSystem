# POS System - Point of Sale, Inventory & Sales Management

ASP.NET Core 10 Web API (Clean Architecture) + PostgreSQL, with an Angular point-of-sale frontend.

**Demo:** [Open Counter POS](https://possystem-1-26mq.onrender.com) ·
**API:** [Swagger UI](https://possystem-1-26mq.onrender.com/swagger/index.html) ·
[OpenAPI JSON](https://possystem-1-26mq.onrender.com/swagger/v1/swagger.json) ·
[Health](https://possystem-1-26mq.onrender.com/health)

This is the deployment URL supplied for the project. The audit environment's network proxy blocks
access to that host; current public health and the deployed commit are not independently verified.
Free Render instances may take time to wake up. Login accounts and secrets are not published.

[Requirements audit](docs/requirements-audit.md) · [API reference](docs/api.md) ·
[Architecture, full ERD, checkout and deployment diagrams](docs/architecture.md) ·
[CI/CD setup](docs/ci-cd.md) · [Free demo deployment](docs/free-demo.md)

## Technology stack
- **Frontend:** Angular 22, TypeScript, standalone components, RxJS, Chart.js dashboard charts with accessible tabular summaries
- **Backend:** .NET 10, ASP.NET Core Web API, Clean Architecture, JWT access tokens and rotating HttpOnly refresh cookies
- **Database:** PostgreSQL with EF Core migrations, global soft-delete filters, filtered indexes, and transaction/concurrency safeguards
- **Background work and mail:** Hangfire PostgreSQL storage, MailKit SMTP, low-stock and daily/monthly sales email jobs
- **Reporting:** ClosedXML `.xlsx` sales/returns and inventory exports; downloadable invoice PDFs
- **Quality and hosting:** xUnit, Angular/Vitest, GitHub Actions, Docker API image, Vercel SPA configuration

## Current capabilities
- JWT auth with refresh-token rotation (refresh token in an HttpOnly cookie, stored hashed)
- Roles: Admin, Manager, Cashier; Admin-managed staff accounts, role changes, account disablement, and password resets
- Categories, products (search, pagination), stock adjustments with a full stock movement history
- Supplier contact management, optional product default suppliers, and purchase orders with Draft -> Ordered -> Received/Cancelled states; receiving stock, movement, and audit updates commit together
- Customer contact records, optional sale association, configurable loyalty earning/redemption, and an auditable loyalty ledger with proportional return reversals
- Manager-managed fixed/percentage coupons with UTC validity windows and redemption limits; coupons are applied after manual line discounts and before loyalty redemptions
- Product image upload, primary image selection, catalog thumbnails, and image deletion; uploads accept PNG/JPEG/WebP up to 4 MB
- Cashier shifts: Open -> PendingClose -> Closed, with cash variance
- POS sales: transactional stock deduction, race-free invoice numbers, product concurrency protection (xmin), user-scoped idempotent checkout retries
- Partial sale returns with proportional refunds, optional recorded return reasons, automatic restocking, and cash-refund shift reconciliation
- Cash checkout validates tender against authoritative server pricing and persists tender/change with the idempotent sale; Cash/Card/Online record methods and do not initiate or settle external payments
- Hangfire jobs stored in PostgreSQL: durable low-stock alert records with retry/delivery state and manager visibility, plus daily/monthly sales and inventory summaries (SMTP)
- Soft delete via EF Core global query filters, automatic audit trail (old vs new values)
- Angular POS, inventory, sales, shift, dashboard, settings, and manager-only audit/stock history screens; inventory and sales lists are paginated, and the dashboard includes daily/monthly revenue, top products, and category sales charts aggregated server-side
- Manager-only Excel report downloads with gross sales, refunds, and net totals
- Swagger UI, `/health`, global error handling, Docker, GitHub Actions CI/CD

### Authentication and session security

Refresh rotation uses a database transaction and a conditional update of the presented token.
Only one request can claim an unrevoked token; presenting a rotated token again revokes the
user's remaining refresh sessions. Refresh/logout (and login, which also issues the cookie)
require an `Origin` matching one of the exact `Cors:Origins` entries or the API's own verified
origin (for Swagger). Missing, `null`, malformed, and unrelated origins are rejected. Do not
replace this policy with a wildcard when allowing a new frontend origin.

Access JWTs carry an HMAC authorization stamp derived from the user's password hash, role,
and active status. Authentication checks that stamp against the current user record on each
authorized request, so role changes, deactivation, and password resets invalidate existing
access tokens as well as refresh sessions. Staff role/status updates and password resets revoke
active refresh tokens when saved.

Login throttling is process-local: five failures for an address/username pair or twenty for an
address within a 15-minute window return `429` with `Retry-After`. For multi-instance deployments,
use a shared distributed limiter so requests cannot evade the per-process counters.

Behind a hosting reverse proxy, configure the actual trusted peer addresses before enabling
forwarding; otherwise clients can appear to share the proxy's rate-limit bucket. Set
`ReverseProxy__Enabled=true`, then provide `ReverseProxy__KnownProxies__0` with a specific trusted
proxy IP or `ReverseProxy__KnownNetworks__0` with a specific trusted CIDR, and set
`ReverseProxy__ForwardLimit` to the actual number of trusted hops (1–10). Forwarding defaults to
disabled and enabling it without explicit trust entries fails startup; universal networks are
rejected. Do not enable the trust-all `ASPNETCORE_FORWARDEDHEADERS_ENABLED` shortcut. Configure
`AllowedHosts` to the public API host(s). Forwarded host headers are intentionally ignored; configure
the proxy to preserve the public Host and validate its host routing. Use your provider's current
network documentation for peer ranges rather than trusting arbitrary X-Forwarded-For values.

## Run locally

For a free portfolio deployment using one Render service and Neon PostgreSQL, follow
[the free demo guide](docs/free-demo.md). The included `render.yaml` serves both the client
and API and stores demo product images in PostgreSQL so no paid persistent disk is needed.

When upgrading, deploy the API and frontend together: cash sale requests now require a
cent-precision `cashTendered` amount covering the server's current total, and responses include
the persisted `cashTendered` and `changeDue`. The `AddCashTenderAndChange` migration runs at
API startup and leaves those fields null on historical sales.
Prerequisites: .NET 10 SDK, Docker (or any local PostgreSQL; edit `ConnectionStrings:Default`).

```bash
dotnet restore
dotnet build -c Release
docker compose up -d db         # PostgreSQL
dotnet run --project src/PosSystem.Api --urls http://localhost:5080
```
`setup.sh` is the original project scaffolder, not a dependency installer for an existing checkout;
do not rerun it over application source. The API applies committed migrations and seeds the local
development account on startup.
To run the API container with the local-only development configuration:
`docker compose --profile full up --build` (API at `http://localhost:8080`). The Compose profile
explicitly uses Development mode and disabled SMTP so it does not require production-only credentials.
Swagger: http://localhost:5080/swagger - default login `admin` / `Admin@12345` (change via `Seed__AdminPassword`).

Run the Angular app in a second terminal:

```bash
cd web
npm ci
npm start
```

The development proxy forwards `/api` and `/health` to `http://127.0.0.1:5080`; update
`web/proxy.conf.json` if you run the API on another local port.
On macOS 12, use `NG_BUILD_SASS_EMBEDDED=false npm run build` and
`NG_BUILD_SASS_EMBEDDED=false npm test -- --watch=false` to use the compatible pure-JavaScript Sass runtime.
For a production build, set `POS_API_ORIGIN` to the deployed API HTTPS origin; the build intentionally
fails instead of producing a frontend with an unset API origin.

### Email and background jobs
Local development email is disabled by default. For a deployment where email delivery is required,
set `Email__Enabled=true` and configure `Email__SmtpHost`,
`Email__SmtpPort`, `Email__SenderAddress`, and SMTP credentials (`Email__Username` and
`Email__Password`) through environment variables or your deployment's secret manager. Use
`Email__UseStartTls=true` for STARTTLS (for example, SMTP port 587), or set
`Email__UseStartTls=false` and `Email__UseSslOnConnect=true` for implicit TLS (commonly port 465).
Never commit SMTP credentials. If the SMTP provider does not require authentication, leave both
username and password blank. The API validates the server configuration at startup when email is enabled.

Sign in as Admin or Manager and open **Settings** to set the report recipient and toggle low-stock
alerts. Admins can send a test message from that page. The daily report runs at midnight UTC and
summarizes the previous UTC day; the monthly report runs at 00:15 UTC on the first day of the month
and summarizes the previous UTC calendar month. Both include gross sales, refunds, net revenue, and
low-stock inventory. Hangfire stores its queue and recurring-job state in PostgreSQL. Low-stock
alerts are queued after stock changes and require the alert toggle to be enabled.

Returns record the selected refund method for accounting; they do not initiate card or online payment
processor transactions. Cashiers can return their own sales, while Admins and Managers can return
any sale. Each return must be processed during the user's open shift.

## Architecture
`Domain` (entities, pricing rules) <- `Application` (services, DTOs, abstractions) <- `Infrastructure` (EF Core, JWT) <- `Api` (controllers, composition root).

The [complete architecture document](docs/architecture.md) contains the system diagram, full ERD,
checkout/payment sequence, dependency diagram, and deployment pipeline. The Render container hosts
both the Angular client and API; PostgreSQL holds business data, images and durable Hangfire jobs.

```mermaid
flowchart LR
  Browser[Angular 22 SPA] -->|HTTPS JSON / JWT| Api[ASP.NET Core 10 API]
  Api --> App[Application services]
  App --> Domain[Domain entities and rules]
  Api --> Infra[Infrastructure]
  Infra --> Db[(PostgreSQL)]
  Infra --> Jobs[Hangfire jobs]
  Jobs --> Db
  Jobs --> SMTP[SMTP provider]
  Api --> Db
```

### Entity relationship diagram

```mermaid
erDiagram
  USER ||--o{ REFRESH_TOKEN : owns
  USER ||--o{ CASHIER_SHIFT : works
  CASHIER_SHIFT ||--o{ SALE : contains
  USER ||--o{ SALE : processes
  SALE ||--|{ SALE_ITEM : has
  CATEGORY ||--o{ PRODUCT : groups
  PRODUCT ||--o{ SALE_ITEM : sold_as
  SUPPLIER o|--o{ PRODUCT : default_for
  SUPPLIER ||--o{ PURCHASE_ORDER : supplies
  PURCHASE_ORDER ||--|{ PURCHASE_ORDER_ITEM : contains
  PRODUCT ||--o{ PURCHASE_ORDER_ITEM : ordered_as
  CUSTOMER o|--o{ SALE : associated
  COUPON o|--o{ SALE : applied
  CUSTOMER ||--o{ LOYALTY_TRANSACTION : tracks
  PRODUCT ||--o{ PRODUCT_IMAGE : displays
  PRODUCT ||--o{ LOW_STOCK_ALERT : tracks
  SALE ||--o{ SALE_RETURN : refunded_by
  CASHIER_SHIFT ||--o{ SALE_RETURN : records
  SALE_RETURN ||--|{ SALE_RETURN_ITEM : includes
  SALE_ITEM ||--o{ SALE_RETURN_ITEM : returned_from
  PRODUCT ||--o{ STOCK_MOVEMENT : tracks
  USER ||--o{ STOCK_MOVEMENT : performs

  USER {
    int Id PK
    string Username
    string Email
    int Role
    bool IsActive
    bool IsDeleted
  }
  PRODUCT {
    int Id PK
    int CategoryId FK
    int DefaultSupplierId FK
    string Sku UK
    int StockQuantity
    int MinStockThreshold
    bool IsDeleted
  }
  CATEGORY {
    int Id PK
    string Name UK
    bool IsDeleted
  }
  SALE {
    int Id PK
    int CashierId FK
    int ShiftId FK
    int CustomerId FK
    int CouponId FK
    string InvoiceNumber UK
    datetime SaleDate
    decimal TotalAmount
    bool IsDeleted
  }
  SALE_ITEM {
    int Id PK
    int SaleId FK
    int ProductId FK
    int Quantity
    decimal LineTotal
  }
  SALE_RETURN {
    int Id PK
    int SaleId FK
    int ShiftId FK
    int ProcessedByUserId FK
    decimal TotalAmount
  }
  SUPPLIER {
    int Id PK
    string Name
    string Email
    string Phone
    bool IsDeleted
  }
  PURCHASE_ORDER {
    int Id PK
    int SupplierId FK
    int CreatedByUserId FK
    int Status
    datetime OrderedAt
    datetime ReceivedAt
  }
  PURCHASE_ORDER_ITEM {
    int Id PK
    int PurchaseOrderId FK
    int ProductId FK
    int Quantity
    decimal UnitCost
  }
  LOW_STOCK_ALERT {
    int Id PK
    int ProductId FK
    datetime DetectedAt
    datetime SentAt
    datetime ResolvedAt
    int DeliveryAttempts
  }
  CUSTOMER {
    int Id PK
    string Name
    string Email
    int LoyaltyPoints
    bool IsDeleted
  }
  COUPON {
    int Id PK
    string Code UK
    bool IsPercentage
    decimal Value
    datetime StartsAt
    datetime EndsAt
    int RedemptionCount
  }
  LOYALTY_TRANSACTION {
    int Id PK
    int CustomerId FK
    int SaleId FK
    int SaleReturnId FK
    int PointsChange
    int BalanceAfter
  }
  PRODUCT_IMAGE {
    int Id PK
    int ProductId FK
    string StorageKey
    string ContentType
    bool IsPrimary
  }
  AUDIT_LOG {
    int Id PK
    int UserId "Logical actor id"
    datetime Timestamp
    string Action
    string EntityName
    string EntityId
    jsonb OldValues
    jsonb NewValues
  }
```

Audit actor identifiers are retained as logical references, not enforced foreign keys. Only entities
shown with `IsDeleted` use global soft-delete filters; financial/history ledgers are retained.

### API endpoint overview

Interactive OpenAPI documentation is available at `/swagger` on a running API. The
[API reference](docs/api.md) lists every controller operation and explains auth, payloads, dates,
report downloads and checkout idempotency.

| Area | Endpoints | Access |
|---|---|---|
| Authentication | `POST /api/auth/login`, `/refresh`, `/logout`; `GET /api/auth/me` | Public login/refresh; authenticated identity |
| Categories | `GET/POST /api/categories`, `PUT/DELETE /api/categories/{id}` | Authenticated reads; Admin/Manager writes |
| Products | `GET/POST /api/products`, `GET/PUT/DELETE /api/products/{id}`, `GET /api/products/by-sku/{sku}`, `POST /api/products/{id}/adjust-stock` | Authenticated reads; Admin/Manager writes |
| Suppliers and purchasing | `/api/suppliers`; `/api/purchase-orders` (create, edit draft, order, receive, cancel) | Admin/Manager |
| Customers and loyalty | `/api/customers`, `/api/customers/{id}/loyalty` | Authenticated reads; Admin/Manager management |
| Coupons and loyalty rules | `/api/coupons`, `/api/coupons/available`, `/api/coupons/loyalty-rules` | Authenticated availability; Admin/Manager management |
| Product images | `/api/products/{id}/images` (list/upload), `PUT /api/products/{id}/images/{imageId}/primary`, `DELETE /api/products/{id}/images/{imageId}` | Admin/Manager |
| Product image files | `GET /api/products/{id}/images/{imageId}` | Public catalog media |
| Sales and returns | `GET/POST /api/sales`, `GET /api/sales/{id}`, `GET/POST /api/sales/{id}/returns` | Authenticated; cashier sales are scoped by service permissions |
| Low-stock alerts | `GET /api/operations/low-stock-alerts` | Admin/Manager |
| Background queue | `GET /api/operations/jobs` | Admin/Manager |
| Shifts | `GET /api/shifts/current`, `POST /api/shifts/open`, `/end`, `/close` | Authenticated |
| Dashboard | `GET /api/dashboard?from=YYYY-MM-DD&to=YYYY-MM-DD` | Admin/Manager |
| History | `GET /api/history/audit`, `/api/history/stock-movements` | Admin/Manager |
| Reports | `GET /api/reports/sales.xlsx`, `/api/reports/inventory.xlsx` | Admin/Manager |
| Staff | `GET/POST /api/staff`, `PUT /api/staff/{id}`, `/api/staff/{id}/password` | Admin |
| Email settings | `GET/PUT /api/email-settings`, `POST /api/email-settings/test` | Admin/Manager settings; Admin test mail |
| Health | `GET /health` | Public; includes database connectivity |

### Screenshots

![Sales and inventory dashboard](docs/screenshots/dashboard.png)
![Point-of-sale register](docs/screenshots/pos.png)
![Print-ready invoice](docs/screenshots/invoice.png)

Captured from the running application with a disposable local PostgreSQL database and synthetic
demo records. The invoice image is rendered from a PDF downloaded through the application.
See [screenshot capture instructions](docs/screenshots/README.md) to reproduce them.
[Download the sample invoice PDF](docs/screenshots/sample-invoice.pdf).

### Product image storage

Image metadata is stored in PostgreSQL. The Render blueprint uses `ProductImages__Storage=Database`,
which also stores the bytes in PostgreSQL; database backups include both. File-mode deployments
store image bytes in `ProductImages__Directory` (default `App_Data/product-images` under the API
content root) and must back up that directory along with the database.
The files are deliberately served as public catalog media so browser image requests do not need to
attach an access token; only upload and deletion require an Admin or Manager. Upload only images
intended for public display. On hosts with ephemeral filesystems, attach persistent storage and set
`ProductImages__Directory` to its mounted path, or uploaded files will not survive replacement of
the container.

## Deploy and share

The demo is configured for the Render link above. For a new deployment, follow the
[single-service Render + Neon guide](docs/free-demo.md), or the separate API/frontend option below.
Free-plan sleep behavior, storage quotas, and database retention depend on the hosting provider.

### 1. Create PostgreSQL

Create a PostgreSQL database with a provider such as Neon or Supabase. Copy its connection string and
check the provider's guidance for SSL/pooling. The API applies EF Core migrations on startup, so use an
empty database or a compatible database previously used by this app.

### 2. Deploy the API

Create a Docker web service from this repository (for example, on Render) using the repository-root
`Dockerfile`. The container listens on port `10000`; set the service's health-check path to `/health`.
The health check verifies PostgreSQL connectivity as well as API health.
Configure these environment variables in the provider dashboard, never in source control:

| Variable | Value |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection string |
| `Jwt__Key` | Unique random secret of at least 32 characters; the default placeholder is rejected in Production |
| `Cors__Origins__0` | Exact public Vercel origin, such as `https://your-pos.vercel.app` |
| `Seed__AdminUsername` | Initial admin username |
| `Seed__AdminEmail` | Initial admin email |
| `Seed__AdminPassword` | Unique initial admin password of at least 12 characters; the default password is rejected in Production |
| `Email__Enabled` | `true` to enable SMTP alerts/reports, or `false` to run without email |
| `Email__SmtpHost` | SMTP hostname from your email provider |
| `Email__SmtpPort` | Provider SMTP port (commonly `587` with STARTTLS) |
| `Email__SenderAddress` | Verified sender address allowed by your provider |
| `Email__Username` / `Email__Password` | Provider SMTP credentials stored as secrets; leave both empty only if SMTP does not require authentication |
| `Email__UseStartTls` | `true` for STARTTLS; `false` for implicit TLS |
| `Email__UseSslOnConnect` | `false` for STARTTLS; `true` for implicit TLS (commonly port `465`) |

SMTP is optional; the API starts with email disabled, but report and low-stock email jobs are not
scheduled. If email is enabled, invalid SMTP settings fail startup. Obtain the hostname, port,
TLS mode, sender verification, and credentials from your SMTP provider (for example, use a provider
issued SMTP credential or an app password where supported; do not use your normal mailbox password).
Keep credentials in the hosting provider's secret environment-variable settings. After deployment,
open Settings, save a valid manager email recipient, and use **Send test email** before relying on
scheduled reports or alerts. The Excel download remains available even if SMTP is temporarily
unavailable after startup.

After the first healthy API deployment, verify `/health` and `/swagger`, then sign in with the initial
admin credentials. Create personal Admin and Manager accounts in Settings as needed, confirm they can
sign in, then disable the default seeded account if you changed the initial login configuration.
Do not share the seeded password publicly.

### 3. Deploy the Angular app

Import the repository as a Vercel project with root directory `web` and output directory
`dist/web/browser`. Configure the project build environment variable `POS_API_ORIGIN` to the exact
deployed API origin (HTTPS origin only, no path or trailing slash). `npm run build` fails if it is
missing or invalid and generates a runtime config containing that origin; the committed frontend
contains no deployment placeholder. API requests use that absolute origin, so they cannot be
mistakenly served as SPA fallback HTML by Vercel. The Angular SPA rewrite remains for frontend routes.

Set `Cors__Origins__0` on the API to the exact public Vercel origin, then redeploy the API. Refresh
tokens are Secure, HttpOnly cookies; browser refresh/logout additionally require an allowed Origin.
The default Vercel and Render hostnames are different sites, and some browsers block third-party
cookies regardless of `SameSite=None`. For dependable refresh cookies, use custom app/API hostnames
under the same registrable domain (for example, `app.example.com` and `api.example.com`) and configure
the corresponding exact CORS/Origin entry. Cross-site cookie behavior must be tested in target browsers.

Open the public Vercel URL and sign in with an Admin account to configure categories/products and
demonstrate the POS. The API and database must both remain available for the demo.

### 4. CI-gated deploys

GitHub Actions runs the .NET build/unit tests, Angular tests/production build, real PostgreSQL
integration tests, Chromium browser workflows, database/image backup-restore checks, and a combined
container smoke test on every branch push and pull requests targeting `main`.
Integration tests create and delete their own databases;
browser tests receive a separate disposable database. Local macOS Sass limitations do not affect
the Linux build. Test result/trace artifacts are retained when appropriate.

For an API deployment only after all readiness jobs pass, disable Render's automatic deploy and configure
the repository Actions secret `RENDER_DEPLOY_HOOK`. For the frontend, disable Vercel's automatic Git
deploy and configure a Vercel Deploy Hook as the repository Actions secret `VERCEL_DEPLOY_HOOK`.
The deploy job runs only for a push to the repository variable `DEPLOY_BRANCH` (default:
`feature/release-readiness`, matching `render.yaml`), after all six readiness jobs succeed. If a hook
is absent, that hosting deploy is skipped; the deployment provider, API/database, SMTP, CORS, and
secrets must still be separately configured. These repository files do not mean a provider account
or live deployment exists.

When changing deployment branches, update both `DEPLOY_BRANCH` and the provider/blueprint branch.
See [CI/CD setup](docs/ci-cd.md). Do not put database URLs, JWT keys, SMTP passwords,
or deploy hooks in committed files or screenshots.

### Database and product-image backup/restore

The database contains image metadata, while `ProductImages__Directory` contains the files. Recover
both together. A database-only dump cannot restore uploaded images. Use PostgreSQL client tools
matching the server major version, Python 3.11+, and a persistent image directory. Supply libpq
connection configuration (`DATABASE_URL` or `PGHOST`/`PGPORT`/`PGDATABASE`/`PGUSER` plus secure
authentication) through the environment/secret manager. Do not pass the application's .NET
connection string as `DATABASE_URL` or put credentials into shell history.

Pause application and background-job writes for the entire backup so the SQL snapshot and copied
image files describe the same state. A bundle contains `database.dump`, `product-images/`, and
`manifest.json` with SHA-256 hashes of every file. It is created with private permissions and never
overwrites an existing bundle. `BACKUP_WRITES_PAUSED=1` acknowledges the operator has paused writers;
the script does not stop a remote application itself.

```bash
# Connection variables are already securely injected; image path is the persistent source.
BACKUP_WRITES_PAUSED=1 PRODUCT_IMAGES_DIRECTORY=/persistent/product-images \
  ./scripts/backup-state.sh /secure-backups/pos-snapshot
python3 scripts/backup-manifest.py verify /secure-backups/pos-snapshot
```

For a Compose development database, avoid installing host client tools by exporting
`PG_DOCKER_CONTAINER=possystem-db-1 PGUSER=postgres PGDATABASE=possystem`. The same scripts use the
PostgreSQL client's container version. Hosted deployments should use their supported secure
connection and storage backup mechanisms instead.

Restore into an **empty, explicitly selected database** and a new/empty image directory with all
application/job processes stopped. The script verifies the complete manifest before touching the
database, refuses an existing database's objects or nonempty image storage, and uses an atomic
database transaction with `pg_restore --exit-on-error`. The acknowledgement below is required for
noninteractive operation; it is not a command to erase an existing production database.

```bash
# Set connection variables to a freshly created disposable restore target first.
RESTORE_TARGET_CONFIRMED=1 ./scripts/restore-state.sh \
  /secure-backups/pos-snapshot /restore-test/product-images
```

Then point a test API's connection and `ProductImages__Directory` to those restored targets, let it
apply any newer committed migrations, check `/health`, sign in, and download a restored product
image before serving traffic. Store encrypted/offsite copies with a defined retention policy.
The SHA-256 manifest detects corruption; it does not encrypt or authenticate a backup against an
attacker who can replace both content and manifest. Keep bundles in access-controlled storage.

Run the automated disposable restore check locally (also required in CI):

```bash
PG_DOCKER_CONTAINER=possystem-db-1 PGUSER=postgres ./scripts/test-backup-restore.sh
```

It creates two uniquely named test databases, restores a row and its matching image bytes, checks
that corruption/nonempty targets are rejected, and deletes its fixtures. Legacy
`backup-database.sh`/`restore-database.sh` remain database-only utilities; the latter also refuses
nonempty databases and still prompts before restore.

### Running readiness checks locally

```bash
dotnet build -c Release
dotnet test tests/PosSystem.Tests -c Release --no-build
# Test-only PostgreSQL admin server; tests own and delete pos_it_* databases.
POS_TEST_POSTGRES='Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=postgres' \
  dotnet test tests/PosSystem.IntegrationTests -c Release --no-build
cd web
npm ci
npm test -- --watch=false
POS_API_ORIGIN=https://your-api.example.com npm run build
npx playwright install --with-deps chromium
# Create a separate disposable pos_e2e database. Never target a real register database.
POS_E2E_CONNECTION='Host=127.0.0.1;Port=5432;Database=pos_e2e;Username=postgres;Password=postgres' npm run e2e
```

PostgreSQL values above are only the documented local Compose defaults. Integration tests skip
when `POS_TEST_POSTGRES` is absent; CI sets it explicitly so unavailable PostgreSQL fails the job.
Browser tests start their test API/frontend processes and require the disposable database, rather
than relying on an already-running development instance. SMTP capture tests exercise real SMTP/TLS
locally without sending mail to external recipients.

### Production verification

Repository tests cannot provision provider accounts or establish that a live deployment works.
Before enabling real-store traffic, verify the actual public frontend/API HTTPS origins and exact
CORS entries, the database connection/health, persistent image disk and backup retention, and SMTP
sender verification/credentials. Use the application test-email flow and confirm delivery in a
controlled manager mailbox. Hook configuration, a successful build, or a local SMTP capture is not
proof of external delivery.

Configure provider credentials and secret values through the hosting/environment settings, never
in source, shell history, test artifacts or chat. No live Render/Vercel/Supabase/Brevo account or
delivery is implied by the configuration templates. Local TLS SMTP capture proves the mail code
can deliver to its test server; public deployment and real mailbox delivery require the configured
provider account and remain separate checks.

Test each supported browser against the real public origins: login, reload to restore its refresh
cookie, let access expire and retry a protected request, use two tabs, logout, and verify a disabled
account loses access. Local proxy browser tests do not reproduce all third-party-cookie policies;
prefer app/API subdomains under one registrable domain when browser policy blocks cross-site cookies.
Complete a test shift/sale/return/close, download an invoice and uploaded product image, and inspect
the scheduled-job delivery/failure state before accepting production transactions.
