# Shannana ERP

Internal ERP for Shannana Foods: master data, then inventory, purchasing, sales/distribution and accounting.
Standalone from the public website (`backend/` + `frontend/`): own database, own users.

**Stack:** ASP.NET Core (.NET 10) Web API + Angular, SQL Server via EF Core. UI in Arabic (RTL) and English.

## Status
| Phase | Scope | State |
|---|---|---|
| 1 | Foundation + master data (auth, roles, items, units, warehouses, suppliers, customers, drivers, vehicles, currencies, rates, users) | done |
| 2 | Inventory and warehouses (stock balances, movement ledger, transfers, count/opening/write-off adjustments, low-stock, valuation) | done |
| 3 | Purchasing (orders, goods receipts, supplier invoices, payments, supplier statement and balances) | done |
| 4 | Sales and distribution (sales orders, delivery notes, distribution trips, sales invoices, returns, customer receipts, customer statement and balances) | done |
| 5 | Accounting | planned |

## Layout
```
src/Erp.Domain          entities, roles and policies
src/Erp.Application     response envelope, validators (FluentValidation)
src/Erp.Infrastructure  EF Core DbContext + migrations, Identity, JWT, seeder
src/Erp.Api             controllers, auth, CSRF, serves the Angular build from wwwroot
tests/Erp.Tests         xUnit integration tests (in-memory DB)
web/                    Angular app (proxy to the API in dev)
```

## Run (development)
Prerequisites: .NET 10 SDK, Node 20+, SQL Server (default local instance, Windows auth).

```powershell
cd erp
dotnet run --project src/Erp.Api        # http://localhost:5004 - creates + migrates the ShannanaErp DB and seeds on first run
cd web
npm install
npm start                               # http://localhost:4200 (proxies /api to :5004)
```
Dev login (from `appsettings.Development.json`, **development only**): `admin@shannana.local` / `Admin#12345`.

Single-origin build (what production uses): `cd web && npx ng build` writes into `src/Erp.Api/wwwroot`, and the API then serves the UI at http://localhost:5004.

## Configuration
| Key | Purpose |
|---|---|
| `ConnectionStrings:Default` | SQL Server connection string |
| `Jwt:Key` | JWT signing secret, **32+ chars, required** (the app refuses to start without it) |
| `Database:AutoMigrate` | `true` to migrate + seed at startup outside Development |
| `Seed:AdminEmail` / `Seed:AdminPassword` | first admin, created only when no users exist |
| `Inventory:AllowNegativeStock` | `false` by default: an issue/transfer that would take stock below zero is rejected |
| `RateLimit:LoginPermitLimit` | failed+successful logins allowed per IP per 15 minutes (default 10) |
| `Seed:LocalCurrencyCode` | local (base) currency, default `JOD`; **fixed after the first run** |

For production set these through environment variables (`Jwt__Key`, `ConnectionStrings__Default`, ...) or user-secrets, never in committed files.

## Inventory rules (phase 2)
- **Ledger first.** `StockMovement` is append-only (+in / -out, with unit cost); `StockBalance` (quantity + moving-average cost per item and warehouse) is maintained in the same transaction.
- **Documents** (transfers, adjustments) are `Draft -> Confirmed -> Cancelled`. Only confirming touches stock; drafts are freely editable.
- **Confirm is atomic**: movements, balances and status commit together or not at all. Balances carry a row-version, so two simultaneous postings to the same stock cannot both succeed (the loser gets a 409/400).
- **Costing**: moving average. Receiving blends the cost; issuing uses the current average; a transfer carries the source cost to the destination.
- **Adjustments** state the *counted* quantity; the difference from the system quantity is posted at confirm time (surplus at the stated cost, else current average, else the item's standard cost).
- **Cancelling a confirmed document** posts reversal movements. It is refused if the stock has since been used (e.g. already transferred on).
- Items and warehouses that still hold stock cannot be deactivated.
- Document numbers are id-based (`TRF-000123`, `ADJ-000045`), unique but not guaranteed gap-free.

## Purchasing rules (phase 3)
- **Flow:** purchase order (Draft -> Confirmed) -> goods receipt (confirm = stock in) -> supplier invoice (confirm = payable) -> payment (on account). Receipts and invoices can also stand alone (direct receipt, no order).
- **Currency:** each document stores the exchange rate it was saved with (local currency = 1; otherwise the rate you enter, else the latest stored rate on/before the date, else the save is refused). Stock is valued in local currency: unit price x rate.
- **Order progress:** confirmed receipts raise `receivedQuantity`, confirmed invoices raise `invoicedQuantity`. A receipt cannot exceed the remaining ordered quantity, and an invoice cannot exceed the received-but-not-yet-invoiced quantity.
- **Cancelling:** an order with confirmed receipts/invoices cannot be cancelled; a receipt cannot be cancelled while invoices cover its quantities or after its stock was used; cancelling a confirmed receipt reverses the stock movements.
- **Supplier account** (statement / balances, local currency): confirmed invoices raise what you owe, confirmed payments lower it. Payments are on account (not matched to invoices).
- **Not in this phase** (accounting phase will handle them): price variances between receipt and invoice, exchange-rate differences, purchase returns, tax.
- **Access:** purchasing data carries cost prices, so reading needs Admin, Accountant, Purchasing or StoreKeeper (Sales and Dispatcher get 403). Writing: orders and invoices - Admin/Purchasing; goods receipts - Admin/Purchasing/StoreKeeper; payments - Admin/Accountant.

## Sales and distribution rules (phase 4)
- **Flow:** sales order (Draft -> Confirmed) -> delivery note (confirm = stock out) -> sales invoice (confirm = receivable). Deliveries and invoices can also stand alone. Same money rules as purchasing (rate snapshot per document, local-currency stock valuation).
- **Order progress:** a delivery cannot exceed the remaining ordered quantity; an invoice cannot exceed the delivered-but-not-yet-invoiced quantity. Orders with confirmed deliveries/invoices cannot be cancelled; a delivery cannot be cancelled while invoices cover it or after its trip has left.
- **Cost of goods sold:** a confirmed delivery records the warehouse's average cost on each line (`unitCost`) for the accounting phase.
- **Credit limit:** confirming an invoice is refused if it would push the customer's balance (confirmed invoices - returns - receipts, local currency) above their credit limit. 0 = no limit.
- **Returns:** a confirmed return puts goods back in stock at the warehouse's current average cost (so the average is not distorted) and credits the customer. They are standalone (not matched to an invoice).
- **Customer account** (statement / balances): invoices raise what the customer owes; returns and receipts lower it. Receipts are on account.
- **Trips:** a trip = driver + vehicle + ordered stops (delivery notes). Planned (editable) -> In transit (needs every delivery confirmed; record Delivered/Failed per stop) -> Completed (no Pending stops). One driver / one vehicle per day. A delivery can be on one live trip at a time; a **delivered** stop is final, a **failed** one can be planned again. A failed stop does not move stock back by itself: cancel the delivery note if the goods return.
- **Access:** reading sales data needs Admin, Accountant, Sales, Dispatcher or StoreKeeper (Purchasing gets 403). Writing: orders and invoices - Admin/Sales; deliveries and returns - Admin/Sales/StoreKeeper; trips - Admin/Dispatcher; customer receipts - Admin/Accountant.
- **Not in this phase:** tax, price lists/discounts, returns matched to invoices, vehicle capacity checks (items have no weight yet).

## Auth and roles
JWT in an HttpOnly cookie (`erp_auth`) plus a double-submit CSRF token (`erp_csrf` cookie echoed in `X-CSRF-Token`) on writes.
Roles: Admin, Accountant, StoreKeeper, Purchasing, Sales, Dispatcher. Everyone signed in can read master data; writes are limited per area
(see `Erp.Domain/Common/Roles.cs`, mirrored for the UI in `web/src/app/core/models.ts`). Deactivated users lose access immediately.

## Tests
```powershell
cd erp
dotnet test
```

## Database changes
```powershell
dotnet ef migrations add <Name> --project src/Erp.Infrastructure --startup-project src/Erp.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/Erp.Infrastructure --startup-project src/Erp.Api
```
