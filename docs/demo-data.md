# Synthetic demo data

Use these values to exercise the deployed portfolio demo. They are deliberately fake. Create
records in the order shown so foreign-key selections are available.

## Staff

Keep the seeded `admin` account for setup. From **Settings → Staff accounts**, create:

| Field | Cashier | Manager |
|---|---|---|
| Username | `demo.cashier` | `demo.manager` |
| Email | `cashier@example.test` | `manager@example.test` |
| Temporary password | `CashierDemo!2026` | `ManagerDemo!2026` |
| Role | Cashier | Manager |

Use the Cashier account to test register permissions and the Manager account to test reports,
inventory, purchasing, customers, and alerts.

## Category and products

Create category `Beverages` with description `Cold and hot drinks` and category `Snacks` with
description `Packaged snacks for the demo counter`.

Create these products. Select the matching category and leave each product active.

| SKU | Name | Description | Cost | Sale price | Tax % | Stock | Minimum stock | Supplier |
|---|---|---|---:|---:|---:|---:|---:|---|
| `COF-250` | House Coffee 250g | Arabica ground coffee | 5.00 | 8.50 | 5 | 25 | 5 | Demo Foods Ltd |
| `TEA-100` | Green Tea 100 bags | Caffeine-free demo tea | 3.00 | 6.00 | 5 | 12 | 4 | Demo Foods Ltd |
| `CHP-050` | Chocolate Bar | 50g chocolate bar | 0.50 | 1.25 | 0 | 40 | 8 | Sweet Supply Co |
| `LOW-001` | Low Stock Sample | Used to trigger an alert | 1.00 | 2.00 | 0 | 2 | 5 | Sweet Supply Co |

Upload a small PNG or JPEG to `COF-250`, mark it primary, then delete a secondary image to
exercise the image manager. Keep images under 4 MB.

## Suppliers and purchase orders

Create these suppliers:

| Name | Contact | Email | Phone | Address | Notes |
|---|---|---|---|---|---|
| Demo Foods Ltd | Aisha Khan | `foods@example.test` | `+1 555 010 0101` | `100 Demo Avenue` | Coffee and tea supplier |
| Sweet Supply Co | Omar Ali | `sweet@example.test` | `+1 555 010 0102` | `200 Sample Street` | Snacks supplier |

Create a purchase order for **Demo Foods Ltd** with `COF-250`, quantity `10`, unit cost `5.00`.
Save the draft, edit it, mark it **Ordered**, then **Receive** it and confirm stock increases.
Create and cancel a second draft order for `TEA-100` to exercise that state transition.

## Customer and loyalty

Create:

| Name | Email | Phone | Address |
|---|---|---|---|
| Demo Customer | `customer@example.test` | `+1 555 010 0200` | `1 Example Road` |

Use this customer on a sale to earn points. Open **Customers → Loyalty history** afterward.

## Coupon and loyalty rules

In Settings, save loyalty rules:

```text
Points earned per currency unit: 1
Points required per currency unit of redemption: 100
```

Create coupon:

| Field | Value |
|---|---|
| Code | `WELCOME10` |
| Name | `Welcome ten percent` |
| Type | Percentage |
| Amount | `10` |
| Starts | Current time |
| Ends | 30 days from now |
| Maximum redemptions | Leave empty |

Apply `WELCOME10` to a sale. Later deactivate it and confirm it is no longer available at the
register.

## Email settings

Use a verified Brevo sender and your own test mailbox in the Manager email field. Do not use the
example addresses above for real delivery. Save the settings, send a test email, and confirm it
appears in Brevo transactional logs and the mailbox.

## Register workflow

1. Open a shift with opening cash `100.00`.
2. Add `COF-250` quantity `2` and `CHP-050` quantity `1`.
3. Select `Demo Customer`, apply `WELCOME10`, and choose **Cash**.
4. Enter cash tendered `25.00` and complete checkout.
5. Print/download the invoice PDF.
6. Confirm stock, dashboard totals, sale history, audit history, and loyalty points.
7. Open the sale in **Sales**, return one `CHP-050` item with refund method **Cash** and reason
   `Customer changed mind`.
8. Confirm stock is restored and the refund appears in the dashboard.
9. End the shift, enter closing cash `100.00` plus the net cash collected, and close the drawer.

For a card test, open another shift and sell one `TEA-100` with **Card**. For an online test,
repeat with **Online**. Cash tendered should be blank for non-cash payments.

## Reports and alerts

Set `LOW-001` stock to `1` with note `Demo low-stock adjustment`. Check **Settings → Low-stock
alerts**, then confirm the configured email receives the alert when SMTP is enabled. Download both
the sales and inventory Excel reports and inspect the generated files.

## Cleanup

After testing, archive the demo customer and deactivate the demo staff accounts and coupon. Keep
the synthetic products and completed sale if the public portfolio demo needs sample content.
