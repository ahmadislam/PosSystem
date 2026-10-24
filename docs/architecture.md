# POS architecture and data model

These diagrams describe the repository's implemented Angular 22 / ASP.NET Core 10 system.
The database is **PostgreSQL**, including the Neon deployment. SQL Server's “non-clustered
index” terminology is represented here by PostgreSQL B-tree indexes; a database-engine
migration is not required to satisfy the indexing objective.

## System architecture

```mermaid
flowchart LR
  Staff["Admin · Manager · Cashier"] --> Client
  subgraph Render["Render: one Docker web service"]
    Client["Angular SPA\nRegister · Shift · Sales · Dashboard\nInventory · Purchasing · Customers · History · Settings"]
    API["ASP.NET Core API\nJWT / current authorization checks\nOrigin checks · validation · exception handling"]
    App["Application services\nPricing · checkout · returns · purchasing · loyalty"]
    Reports["Dashboard SQL aggregates\nClosedXML Excel exports"]
    Persistence["EF Core / Npgsql\nSoft-delete filters · auditing\nTransactions · optimistic concurrency"]
    Worker["Hangfire workers\nLow-stock dispatcher · daily / monthly reports"]
    Client -->|"Same-origin HTTPS REST / bearer JWT"| API
    API --> App
    API --> Reports
    App --> Persistence
    Reports --> Persistence
    Worker --> Persistence
  end
  Persistence --> DB[("Neon PostgreSQL\nBusiness data · audit · images · Hangfire jobs")]
  DB -->|"Persistent alert outbox and job state"| Worker
  Worker -->|"MailKit SMTP / TLS"| Brevo["Brevo or another SMTP provider"]
  Brevo --> Manager["Configured manager mailbox"]
  Client -->|"jsPDF"| Invoice["Invoice PDF download / print"]
```

The Docker image builds and serves both frontend and API. `ClientHosting` serves Angular routes
but never converts unknown `/api`, `/health`, or `/swagger` routes into SPA HTML. Separate
Vercel hosting is also supported through `POS_API_ORIGIN` and exact CORS origins.
Free hosts may sleep: durable jobs survive restarts, but cannot execute while the API is asleep.

## Clean Architecture dependencies

```mermaid
flowchart TB
  API["Api\nControllers · composition root · hosting"] --> Application
  API --> Infrastructure
  Infrastructure["Infrastructure\nEF Core · SMTP · Hangfire · JWT · Excel"] --> Application
  Application["Application\nUse cases · DTOs · abstractions"] --> Domain
  Infrastructure --> Domain
  Domain["Domain\nEntities · pricing · discount allocation · return calculations"]
```

## Entity relationship diagram

Column names match the current entities and migrations. `CreatedAt` / `UpdatedAt` inherited
from `BaseEntity` are omitted for readability. Flags are shown only where soft deletion is
implemented: users, categories, suppliers, products, customers, and sales. Financial line
items, returns, stock movements, loyalty entries, and audit records retain history.

```mermaid
erDiagram
  Users ||--o{ RefreshTokens : owns
  Users ||--o{ CashierShifts : opens
  Users ||--o{ Sales : processes
  Users ||--o{ PurchaseOrders : creates
  Users ||--o{ SaleReturns : processes
  Users ||--o{ StockMovements : records
  Categories ||--o{ Products : groups
  Suppliers o|--o{ Products : default_supplier
  Suppliers ||--o{ PurchaseOrders : supplies
  Products ||--o{ ProductImages : displays
  ProductImages ||--o| ProductImageContents : stores_optional_bytes
  Products ||--o{ LowStockAlerts : triggers
  Products ||--o{ StockMovements : tracks
  Products ||--o{ SaleItems : sold_as
  Products ||--o{ SaleReturnItems : restocked_as
  Products ||--o{ PurchaseOrderItems : ordered_as
  PurchaseOrders ||--o{ PurchaseOrderItems : contains
  CashierShifts ||--o{ Sales : records
  CashierShifts ||--o{ SaleReturns : reconciles
  Customers o|--o{ Sales : associated_with
  Coupons o|--o{ Sales : applied_to
  Sales ||--o{ SaleItems : contains
  Sales ||--o{ SaleReturns : refunded_by
  SaleReturns ||--o{ SaleReturnItems : contains
  SaleItems ||--o{ SaleReturnItems : returned_from
  Customers ||--o{ LoyaltyTransactions : has_ledger
  Sales o|--o{ LoyaltyTransactions : earns_or_redeems
  SaleReturns o|--o{ LoyaltyTransactions : reverses_points

  Users {
    int Id PK
    string Username UK
    string Email UK
    string PasswordHash
    int Role
    bool IsActive
    bool IsDeleted
  }
  RefreshTokens {
    int Id PK
    int UserId FK
    string TokenHash UK
    datetime ExpiresAt
    datetime RevokedAt
    int ReplacedByTokenId
  }
  Categories {
    int Id PK
    string Name UK
    string Description
    bool IsDeleted
  }
  Suppliers {
    int Id PK
    string Name UK
    string ContactName
    string Email
    string Phone
    string Address
    bool IsActive
    bool IsDeleted
  }
  Products {
    int Id PK
    int CategoryId FK
    int DefaultSupplierId FK
    string Sku UK
    string Name
    decimal CostPrice
    decimal SalePrice
    decimal TaxRate
    int StockQuantity
    int MinStockThreshold
    uint RowVersion "PostgreSQL xmin"
    bool IsActive
    bool IsDeleted
  }
  ProductImages {
    int Id PK
    int ProductId FK
    string StorageKey
    string ContentType
    bool IsPrimary
  }
  ProductImageContents {
    int ProductImageId PK,FK
    bytea Bytes
  }
  Customers {
    int Id PK
    string Name
    string Email UK
    string Phone
    string Address
    int LoyaltyPoints
    uint RowVersion
    bool IsActive
    bool IsDeleted
  }
  Coupons {
    int Id PK
    string Code UK
    bool IsPercentage
    decimal Value
    datetime StartsAt
    datetime EndsAt
    bool IsActive
    int MaxRedemptions
    int RedemptionCount
  }
  CashierShifts {
    int Id PK
    int CashierId FK
    datetime StartTime
    datetime EndTime
    int Status
    decimal OpeningCash
    decimal ExpectedCash
    decimal ClosingCash
    decimal CashVariance
  }
  Sales {
    int Id PK
    int CashierId FK
    int ShiftId FK
    int CustomerId FK
    int CouponId FK
    string InvoiceNumber UK
    string IdempotencyKey "Unique with CashierId"
    string RequestHash
    datetime SaleDate "B-tree index"
    decimal Subtotal
    decimal DiscountAmount
    decimal TaxAmount
    decimal TotalAmount
    decimal CashTendered
    decimal ChangeDue
    int PaymentMethod
    string PaymentProvider
    string PaymentReference
    string PaymentStatus
    int Status
    bool IsDeleted
  }
  SaleItems {
    int Id PK
    int SaleId FK
    int ProductId FK
    string Sku "Snapshot at checkout"
    string ProductName "Snapshot at checkout"
    int Quantity
    decimal UnitPrice
    decimal DiscountAmount
    decimal TaxAmount
    decimal LineTotal
  }
  SaleReturns {
    int Id PK
    int SaleId FK
    int ShiftId FK
    int ProcessedByUserId FK
    datetime RefundDate
    int RefundMethod
    string Reason
    decimal TotalAmount
  }
  SaleReturnItems {
    int Id PK
    int SaleReturnId FK
    int SaleItemId FK
    int ProductId FK
    int Quantity
    decimal LineTotal
  }
  PurchaseOrders {
    int Id PK
    int SupplierId FK
    int CreatedByUserId FK
    int Status
    datetime OrderedAt
    datetime ReceivedAt
    int Version
  }
  PurchaseOrderItems {
    int Id PK
    int PurchaseOrderId FK
    int ProductId FK
    int Quantity
    decimal UnitCost
  }
  StockMovements {
    int Id PK
    int ProductId FK
    int CreatedByUserId FK
    int Type
    int QuantityChange
    string ReferenceType
    int ReferenceId
    string Note
  }
  LoyaltyTransactions {
    int Id PK
    int CustomerId FK
    int SaleId FK
    int SaleReturnId FK
    int PointsChange
    int BalanceAfter
    string Type
  }
  LowStockAlerts {
    int Id PK
    int ProductId FK
    datetime DetectedAt
    datetime SentAt
    datetime LastAttemptAt
    datetime NextAttemptAt
    datetime ResolvedAt
    int DeliveryAttempts
    string LastError
  }
  AuditLogs {
    int Id PK
    int UserId "Logical actor id; nullable for system"
    datetime Timestamp
    int Action
    string EntityName
    string EntityId
    jsonb OldValues
    jsonb NewValues
  }
  Settings {
    int Id PK
    string SettingKey UK
    string Value
    int UpdatedByUserId "Logical actor id"
  }
```

`AuditLogs.UserId` and `Settings.UpdatedByUserId` are logical actor identifiers, not enforced
foreign keys in the current model. `StockMovements.ReferenceType/ReferenceId` and
`RefreshTokens.ReplacedByTokenId` likewise do not establish additional database foreign keys.
The separate `hangfire` schema contains Hangfire-managed job tables.

## Checkout and payment sequence

```mermaid
sequenceDiagram
  actor Cashier
  participant UI as Angular Register
  participant API as SalesController / JWT validation
  participant Sale as SaleService
  participant DB as PostgreSQL / audit / outbox
  participant Worker as Hangfire dispatcher
  participant SMTP as SMTP provider
  Cashier->>UI: Scan or select products and choose Cash / Card / Online
  UI->>UI: Preview totals and validate tender / payment reference
  UI->>API: POST /api/sales + Idempotency-Key UUID
  API->>Sale: Authenticated request and current user
  Sale->>DB: Find prior sale for cashier + key
  alt Already committed with identical request hash
    DB-->>Sale: Existing sale
    Sale-->>UI: Original invoice (no duplicate stock deduction)
  else New sale
    Sale->>DB: Begin transaction and lock open cashier shift
    Sale->>DB: Read products, validate stock, lock promotion/customer as needed
    Sale->>Sale: Authoritative prices, discounts, loyalty and tax
    alt Invalid tender, stock, shift, promotion or conflicting request
      Sale->>DB: Roll back transaction
      Sale-->>UI: Validation / conflict response, retain cart
    else Valid transaction
      Sale->>DB: Deduct stock with concurrency checks
      Sale->>DB: Save sale + snapshot lines + loyalty + stock movements
      Sale->>DB: Save old/new audit values and low-stock outbox
      Sale->>DB: Commit atomically
      Sale-->>UI: Invoice, authoritative totals and cash change
      UI->>UI: Clear cart, show completion, print/download PDF
    end
  end
  opt Pending low-stock alert, SMTP and alerts enabled
    Worker->>DB: Claim durable alert after transaction committed
    Worker->>SMTP: Send manager email using TLS
    alt Provider accepts message
      Worker->>DB: Save sent timestamp
    else Delivery fails
      Worker->>DB: Save attempt/backoff for retry
    end
  end
```

Card and online methods **record a provider/reference**, including Easypaisa and JazzCash;
they do not execute a gateway charge. Cash validates tender and persists change. SMTP delivery
occurs after commit, so a mail failure cannot undo a completed sale. Mail delivery and database
state cannot form one distributed transaction; a worker failure after SMTP acceptance can
produce a repeated message on retry.

## Deployment and CI/CD

```mermaid
flowchart TB
  Push["GitHub push / pull request"] --> API[".NET restore · Release build · xUnit"]
  Push --> Web["npm ci · Vitest · Angular production build"]
  Push --> IT["PostgreSQL integration + SMTP/TLS tests"]
  Push --> Browser["Chromium end-to-end workflows"]
  Push --> Backup["Disposable backup / restore checks"]
  Push --> Image["Build combined Docker image · UI/API health smoke test"]
  API & Web & IT & Browser & Backup & Image --> Gate{"All checks pass + push to DEPLOY_BRANCH?"}
  Gate -->|Yes, configured secret| Hook["Render deploy hook"]
  Hook --> Render["Render Docker service\nAngular + API + Hangfire"]
  Render -->|"TLS / Npgsql"| Neon[("Neon PostgreSQL")]
  Render -->|"TLS / MailKit"| Brevo["SMTP provider"]
  Gate -->|"Optional separate frontend hook"| Vercel["Vercel Angular SPA"]
  Vercel -->|"HTTPS REST + exact CORS origin"| Render
```

Hook secrets must be configured by a repository administrator, and provider Git auto-deploy
must be disabled for the CI gate to control releases. See [CI/CD setup](ci-cd.md) for branch
selection, required secrets, and the difference between a successful hook request and a
healthy deployment. No credentials are stored in these diagrams.
