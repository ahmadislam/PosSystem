# Application screenshots

These are captures of the real Angular UI backed by an isolated local PostgreSQL database,
using the optional demo seeder. The invoice image is rendered from a PDF downloaded through
the checkout UI. No production records or provider credentials are used.

- `dashboard.png`: Chart.js monthly revenue, top products, category sales, and existing metrics.
- `pos.png`: populated catalog, active shift, quantity controls, tender and checkout.
- `invoice.png`: first page of `sample-invoice.pdf`.

To refresh the captures, install the repository dependencies, build the API in Release mode,
install Chromium using Playwright (or set `CHROMIUM_EXECUTABLE_PATH`), and install Poppler's
`pdftoppm` command. Create a **fresh, empty disposable** PostgreSQL database, then run:

```bash
cd web
# Inject POS_E2E_CONNECTION using the local test environment; never use a live database.
POS_CAPTURE_DOCS=1 npm run e2e -- e2e/documentation.spec.ts
```

The Playwright config starts its API/frontend on ports 5082/4202, enables demo data only for
this opt-in run, and writes these artifacts. The test makes a sale and closes its demo shift.
Drop the disposable database afterward. Normal CI browser tests skip this capture test.
