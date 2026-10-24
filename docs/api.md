# API reference

Base URL: `https://possystem-1-26mq.onrender.com` (combined Angular/API demo).

- [Swagger UI](https://possystem-1-26mq.onrender.com/swagger/index.html)
- [OpenAPI JSON with request/response schemas](https://possystem-1-26mq.onrender.com/swagger/v1/swagger.json)
- Local development: `http://localhost:5080/swagger`

Public-host reachability was blocked by the audit environment's proxy. Swagger is enabled in
[`Program.cs`](../src/PosSystem.Api/Program.cs); the local browser audit uses the actual generated
OpenAPI document. OpenAPI is the source of truth for payload schemas and response types.

## Authentication and conventions

Send `Authorization: Bearer <accessToken>` for protected endpoints. Login returns an access token
in JSON and issues a rotating Secure/HttpOnly refresh cookie scoped to `/api/auth`. Refresh and
logout use that cookie. Login, refresh and logout require an exact allowed `Origin` header.
For cross-origin frontend requests include credentials and configure exact CORS origins; the
combined Render deployment uses the same origin for client and API.

When `DemoAccess:Enabled` is true, `POST /api/auth/demo` starts a session for the configured
Manager demo account without accepting a username or password from the caller. It uses the same
origin validation, JWT, refresh-cookie rotation, and authorization checks as staff login.

Roles below: **Staff** = any authenticated Admin, Manager or Cashier; **Management** = Admin or
Manager; **Admin** = Admin only. Cashiers can only read/return their own sales, and returns require
the user's open shift. Protected account changes invalidate active JWT authorization stamps.

JSON enums use names such as `Cash`, `Card`, `Online`, `Open`, `PendingClose` and `Closed`.
Date filters accept `YYYY-MM-DD` and include both UTC calendar days. Pagination parameters
are `page` and `pageSize`; paged results contain `items`, `page`, `pageSize`, `totalCount`,
and `totalPages`. Errors use HTTP status codes and a readable `message` (validation responses
may also include field `errors`). See OpenAPI for the exact schema of each request.

## Endpoint inventory

Methods that share a path are listed separately where their permissions differ.

| Method | Path | Access | Purpose / query |
|---|---|---|---|
| POST | `/api/auth/login` | Public + allowed Origin | Username/password; token and refresh cookie |
| POST | `/api/auth/demo` | Public + allowed Origin | Start the configured portfolio demo session without credentials |
| POST | `/api/auth/refresh` | Refresh cookie + allowed Origin | Rotate refresh session and issue access token |
| POST | `/api/auth/logout` | Allowed Origin | Revoke presented refresh session; clear cookie |
| GET | `/api/auth/me` | Staff | Current identity and role |
| GET | `/api/categories` | Staff | Visible category list |
| POST | `/api/categories` | Management | Create category |
| PUT | `/api/categories/{id}` | Management | Update category |
| DELETE | `/api/categories/{id}` | Management | Soft-delete category subject to service rules |
| GET | `/api/products` | Staff | `search`, `categoryId`, `lowStockOnly`, `page`, `pageSize` |
| GET | `/api/products/{id}` | Staff | Product details |
| GET | `/api/products/by-sku/{sku}` | Staff | Exact scanner/SKU lookup |
| POST | `/api/products` | Management | Create product and initial stock |
| PUT | `/api/products/{id}` | Management | Update product metadata/prices |
| POST | `/api/products/{id}/adjust-stock` | Management | Stock delta with movement/audit |
| DELETE | `/api/products/{id}` | Management | Soft-delete product |
| GET | `/api/products/{id}/images` | Management | Image metadata list |
| POST | `/api/products/{id}/images` | Management | Multipart `file`; PNG/JPEG/WebP, at most 4 MB |
| GET | `/api/products/{id}/images/{imageId}` | Public | Catalog image bytes |
| PUT | `/api/products/{id}/images/{imageId}/primary` | Management | Select primary image |
| DELETE | `/api/products/{id}/images/{imageId}` | Management | Delete an image |
| GET | `/api/suppliers` | Management | Supplier list |
| GET | `/api/suppliers/{id}` | Management | Supplier details |
| POST | `/api/suppliers` | Management | Create supplier |
| PUT | `/api/suppliers/{id}` | Management | Update supplier |
| DELETE | `/api/suppliers/{id}` | Management | Soft-delete supplier |
| GET | `/api/purchase-orders` | Management | `status`, `page`, `pageSize` |
| GET | `/api/purchase-orders/{id}` | Management | Order and lines |
| POST | `/api/purchase-orders` | Management | Create draft |
| PUT | `/api/purchase-orders/{id}` | Management | Update draft only |
| POST | `/api/purchase-orders/{id}/order` | Management | Draft → Ordered |
| POST | `/api/purchase-orders/{id}/receive` | Management | Ordered → Received; atomic stock/movement update |
| POST | `/api/purchase-orders/{id}/cancel` | Management | Cancel eligible order |
| GET | `/api/customers` | Staff | Customer list |
| POST | `/api/customers` | Management | Create customer |
| PUT | `/api/customers/{id}` | Management | Update customer |
| DELETE | `/api/customers/{id}` | Management | Soft-delete customer |
| GET | `/api/customers/{id}/loyalty` | Staff | Customer loyalty ledger |
| GET | `/api/coupons` | Management | All configured coupons |
| GET | `/api/coupons/available` | Staff | Currently available coupons |
| POST | `/api/coupons` | Management | Create fixed/percentage promotion |
| PUT | `/api/coupons/{id}` | Management | Update promotion |
| GET | `/api/coupons/loyalty-rules` | Staff | Earning/redemption rules |
| PUT | `/api/coupons/loyalty-rules` | Management | Save earning/redemption rules |
| GET | `/api/shifts/current` | Staff | Current cashier shift (or no content) |
| POST | `/api/shifts/open` | Staff | Opening cash → Open |
| POST | `/api/shifts/end` | Staff | Open → PendingClose |
| POST | `/api/shifts/close` | Staff | Counted cash → Closed and variance |
| GET | `/api/sales` | Staff | `from`, `to`, `page`, `pageSize`; cashier ownership enforced |
| GET | `/api/sales/{id}` | Staff | Invoice with snapshot lines |
| POST | `/api/sales` | Staff | Checkout; requires UUID `Idempotency-Key` header |
| GET | `/api/sales/{saleId}/returns` | Staff | Return history; ownership enforced |
| POST | `/api/sales/{saleId}/returns` | Staff | Partial/full return; requires own open shift |
| GET | `/api/dashboard` | Management | `from`, `to`; aggregated KPIs, daily/monthly/product/category series |
| GET | `/api/reports/sales.xlsx` | Management | `from`, `to`; server-generated sales/refunds/net workbook |
| GET | `/api/reports/inventory.xlsx` | Management | Server-generated stock/valuation workbook |
| GET | `/api/history/audit` | Management | `from`, `to`, `entityName`, `action`, `page`, `pageSize` |
| GET | `/api/history/stock-movements` | Management | `productId`, `from`, `to`, `page`, `pageSize` |
| GET | `/api/operations/jobs` | Management | Hangfire failed/enqueued/scheduled counts |
| GET | `/api/operations/low-stock-alerts` | Management | Latest 100 durable alert/delivery records |
| GET | `/api/staff` | Admin | Staff account list |
| POST | `/api/staff` | Admin | Create staff account |
| PUT | `/api/staff/{id}` | Admin | Change role / active status |
| PUT | `/api/staff/{id}/password` | Admin | Reset password and revoke sessions |
| GET | `/api/email-settings` | Management | SMTP-enabled flag, manager recipient and alert preference |
| PUT | `/api/email-settings` | Management | Save recipient and alert preference |
| POST | `/api/email-settings/test` | Admin | Send test message to saved manager recipient |
| GET | `/health` | Public | API/database health; healthy body is `Healthy` |
| GET | `/swagger/v1/swagger.json` | Public | Generated OpenAPI schema |
| GET | `/swagger/index.html` | Public | Interactive Swagger UI |

## Checkout example

Example body for `POST /api/sales` with a newly generated UUID `Idempotency-Key` header:

```json
{
  "paymentMethod": "Cash",
  "customerId": null,
  "couponCode": null,
  "loyaltyPointsToRedeem": 0,
  "cashTendered": 20.00,
  "items": [
    { "productId": 1, "quantity": 1, "discountAmount": 0 }
  ]
}
```

Use actual product IDs/prices from your test database. The server rechecks stock, open shift,
pricing, discounts, loyalty, tax and tender; it does not trust the cart preview. Retrying the
same unchanged request with the same key returns the original sale. Reusing the key with a
different body is a conflict. Generate a new key for a different sale.

For Card/Online, omit `cashTendered` and provide `paymentReference` plus an optional
`paymentProvider`. These record a payment; they do not initiate or settle a gateway charge.
Easypaisa/JazzCash provider references are supported as recorded online payments.

Report endpoints return `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
and an attachment filename. The PDF invoice is generated in Angular using jsPDF from the
authoritative sale response; there is no separate invoice-PDF API endpoint.
