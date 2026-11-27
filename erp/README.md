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
| 5 | Accounting (chart of accounts, automatic postings from every module, manual journal, ledger, trial balance, income statement, balance sheet, reconciliation, period lock, dashboard KPIs) | done |

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
| `Seed:LocalCurrencyCode` | local (base) currency, default `SYP` (new Syrian pound; USD is the foreign currency); **fixed after the first run** |

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

## Accounting rules (phase 5)
- **Double entry, local currency.** Every journal line is a debit or a credit (never both, never negative) in LOCAL currency; each entry balances to the unit. Documents carry their own exchange-rate snapshot, so foreign-currency documents post at that rate. Foreign-currency revaluation and exchange differences are not modelled.
- **Automatic postings** are staged by `JournalPoster` and committed in the SAME transaction as the document, so a document and its entry can never disagree. Rules (accounts are found by *role*, not code, so the chart can be re-coded):

| Event | Debit | Credit |
|---|---|---|
| Goods receipt confirmed | Inventory | Goods received not invoiced (GRNI) |
| Purchase invoice confirmed | GRNI | Payables (supplier) |
| Supplier payment confirmed | Payables (supplier) | Cash (cash payments) or Bank (transfer / cheque) |
| Delivery note confirmed | Cost of goods sold | Inventory |
| Sales invoice confirmed | Receivables (customer) | Sales revenue |
| Sales return confirmed | Sales returns (contra revenue) + Inventory | Receivables (customer) + Cost of goods sold |
| Customer receipt confirmed | Cash / Bank | Receivables (customer) |
| Stock adjustment (opening balance) | Inventory | Opening balance equity |
| Stock adjustment (count / damage) | Inventory / Inventory adjustments | Inventory adjustments / Inventory |

  Transfers between warehouses post nothing (one inventory account). Cancelling a confirmed document posts a mirror entry today and marks the original *Reversed*; both stay on the books and net to zero.
- **Manual entries:** Draft (editable, deletable) -> Posted -> Reversed (by a mirror entry). Entries generated by documents cannot be edited or reversed by hand: cancel the document.
- **Sub-ledger tags:** payable / receivable lines carry the supplier or customer, so the ledger can be filtered per party and agrees with the supplier / customer statements.
- **Reports:** trial balance (with opening balances for any date range), general ledger with running balance, income statement, balance sheet (current-period profit shown as its own equity line because nothing is closed into retained earnings yet), and a **reconciliation** of Inventory / Payables / Receivables against the stock, supplier and customer sub-ledgers. Stock is valued as quantity x a 4-decimal average cost, so inventory may differ from the ledger by a rounding sliver (<= 0.01 is reported as reconciled); payables and receivables match to the cent.
- **Period lock:** a lock date forbids posting or reversing on or before it, including confirming and cancelling documents dated in that period.
- **Price variance:** if a supplier invoice price differs from the receipt price, the difference stays in GRNI until settled (no variance account yet). Non-stock purchases (services) are not supported.
- **Starting point:** accounting begins with this migration. Documents confirmed before it have no entries (this is a development database; a production roll-out would need a back-fill).
- **Access:** Admin and Accountant only (reports, journal, settings). The chart of accounts itself is readable by everyone signed in; accounts used by automatic postings cannot be deactivated or change type.

## Cross-cutting rules
- **Gap-free numbering.** A draft has no number. The number (PO-000012, SINV-000007, JV-000031 ...) is taken from a per-family counter at CONFIRMATION, inside the same transaction as the document, so a failed or cancelled draft consumes nothing. The counter row has a row-version: two people confirming the same family at the same instant cannot share a number (the loser gets a retry message). Trips are numbered when created (they are never confirmed).
- **Error messages follow the user's language.** The Angular app sends `X-Lang: ar|en` on every call. The code throws plain English messages; `MessageCatalog` translates them at the API boundary (a result filter, the exception handler and the CSRF check), and FluentValidation's own messages and field names come out in Arabic via the request culture and `FieldNames`. A test scans the source and fails if any message literal has no Arabic entry, so a new message cannot ship untranslated.

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
