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
- **Not in this phase** (accounting phase will handle them): price variances between receipt and invoice, exchange-rate differences, tax. (Purchase returns and returns linked to invoices came later, see *Returns*.)
- **Access:** purchasing data carries cost prices, so reading needs Admin, Accountant, Purchasing or StoreKeeper (Sales and Dispatcher get 403). Writing: orders and invoices - Admin/Purchasing; goods receipts - Admin/Purchasing/StoreKeeper; payments - Admin/Accountant.

## Sales and distribution rules (phase 4)
- **Flow:** sales order (Draft -> Confirmed) -> delivery note (confirm = stock out) -> sales invoice (confirm = receivable). Deliveries and invoices can also stand alone. Same money rules as purchasing (rate snapshot per document, local-currency stock valuation).
- **Order progress:** a delivery cannot exceed the remaining ordered quantity; an invoice cannot exceed the delivered-but-not-yet-invoiced quantity. Orders with confirmed deliveries/invoices cannot be cancelled; a delivery cannot be cancelled while invoices cover it or after its trip has left.
- **Cost of goods sold:** a confirmed delivery records the warehouse's average cost on each line (`unitCost`) for the accounting phase.
- **Credit limit:** confirming an invoice is refused if it would push the customer's balance (confirmed invoices - returns - receipts, local currency) above their credit limit. 0 = no limit.
- **Returns:** a confirmed return puts goods back in stock at the warehouse's current average cost (so the average is not distorted) and credits the customer. They may be linked to an invoice (see *Returns* below) or standalone.
- **Customer account** (statement / balances): invoices raise what the customer owes; returns and receipts lower it. Receipts are on account.
- **Trips:** a trip = driver + vehicle + ordered stops (delivery notes). Planned (editable) -> In transit (needs every delivery confirmed; record Delivered/Failed per stop) -> Completed (no Pending stops). One driver / one vehicle per day. A delivery can be on one live trip at a time; a **delivered** stop is final, a **failed** one can be planned again. When a trip is **completed**, the delivery note of every *failed* stop is cancelled automatically (goods back in stock, GL reversed), in the same transaction; if that delivery cannot be cancelled (e.g. it is already invoiced) completion is refused with the reason. Cancelled deliveries do not return to the unassigned pool.
- **Access:** reading sales data needs Admin, Accountant, Sales, Dispatcher or StoreKeeper (Purchasing gets 403). Writing: orders and invoices - Admin/Sales; deliveries and returns - Admin/Sales/StoreKeeper; trips - Admin/Dispatcher; customer receipts - Admin/Accountant.
- **Not in this phase:** tax (undecided). Price lists, discounts and the capacity check are described under *Pricing, discounts and load*.

## Accounting rules (phase 5)
- **Double entry, local currency.** Every journal line is a debit or a credit (never both, never negative) in LOCAL currency; each entry balances to the unit. Documents carry their own exchange-rate snapshot, so foreign-currency documents post at that rate. Foreign-currency revaluation and exchange differences are not modelled.
- **Automatic postings** are staged by `JournalPoster` and committed in the SAME transaction as the document, so a document and its entry can never disagree. Rules (accounts are found by *role*, not code, so the chart can be re-coded):

| Event | Debit | Credit |
|---|---|---|
| Goods receipt confirmed | Inventory | Goods received not invoiced (GRNI) |
| Purchase invoice confirmed | GRNI | Payables (supplier) |
| Supplier payment confirmed | Payables (supplier) | Cash (cash payments) or Bank (transfer / cheque) |
| Delivery note confirmed | Cost of goods sold | Inventory |
| Sales invoice confirmed | Receivables (net) + Sales discounts given (4250) | Sales revenue (at the undiscounted value) |
| Sales return confirmed | Sales returns (contra revenue) + Inventory | Receivables (customer) + Cost of goods sold |
| Customer receipt confirmed | Cash / Bank | Receivables (customer) |
| Purchase return confirmed | Payables (supplier) | Inventory (at average cost) + GRNI for any price/cost difference |
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

## Settlement, expenses and exchange differences
- **Allocation.** A supplier payment / customer receipt can be allocated to open invoices (purchase invoices and expense invoices; sales invoices). Rules: invoice confirmed, same party, same currency as the payment, allocation <= what is still outstanding (confirmed payments only count). Whatever is not allocated stays *on account*. Cancelling a payment frees its allocations; an invoice with confirmed allocations cannot be cancelled.
- **Realised exchange differences** are booked when a payment is confirmed: the allocated part clears the payable / receivable at the INVOICE's rate, while the money moved at the PAYMENT's rate; the difference goes to *Exchange losses* (5300) or *Exchange gains* (4300). What actually cleared is stored on the payment (`LocalSettled`), and statements, balances and the reconciliation use it, so sub-ledgers and the ledger still agree to the cent.
- **Open invoices and ageing** (suppliers and customers): outstanding per invoice, and per party by days past due (not due / 1-30 / 31-60 / 61-90 / 90+) in local currency.
- **Revaluation.** For a closing date the system revalues open foreign-currency invoices at the latest stored rate up to that date, posts the unrealised difference on that date and an automatic reversal the next day (one run per date). Unrealised differences are excluded from the reconciliation (they are timing items). Missing a closing rate for a currency with open invoices is reported by name.
- **Expenses (non-stock).** *Expense invoices* are supplier bills whose lines name an expense account and an amount (Dr expense accounts / Cr Payables, they appear on the supplier statement and can be paid by allocation). *Expense vouchers* are paid on the spot (Dr expense accounts / Cr Cash or Bank, Admin/Accountant). Lines may only use active, postable **Expense**-type accounts; line rounding is adjusted so the lines add up to exactly the payable.
- **Price variance** between a receipt and its invoice is still left in *Goods received not invoiced* for the accountant (the purchase-invoice screen says so).

## Cross-cutting rules
- **Gap-free numbering.** A draft has no number. The number (PO-000012, SINV-000007, JV-000031 ...) is taken from a per-family counter at CONFIRMATION, inside the same transaction as the document, so a failed or cancelled draft consumes nothing. The counter row has a row-version: two people confirming the same family at the same instant cannot share a number (the loser gets a retry message). Trips are numbered when created (they are never confirmed).
- **Error messages follow the user's language.** The Angular app sends `X-Lang: ar|en` on every call. The code throws plain English messages; `MessageCatalog` translates them at the API boundary (a result filter, the exception handler and the CSRF check), and FluentValidation's own messages and field names come out in Arabic via the request culture and `FieldNames`. A test scans the source and fails if any message literal has no Arabic entry, so a new message cannot ship untranslated.

## Auth and roles
JWT in an HttpOnly cookie (`erp_auth`) plus a double-submit CSRF token (`erp_csrf` cookie echoed in `X-CSRF-Token`) on writes.
Roles: Admin, Accountant, StoreKeeper, Purchasing, Sales, Dispatcher. Everyone signed in can read master data; writes are limited per area
(see `Erp.Domain/Common/Roles.cs`, mirrored for the UI in `web/src/app/core/models.ts`). Deactivated users lose access immediately.

## Pricing, discounts and load
- **Two price lists** per item: *Sale price* (retail) and an optional *Distributor price* (local currency). When a sales document is typed, the unit price is suggested from the list matching the customer's type (distributor price for Distributor customers that the item has one for, retail otherwise), converted to the document currency; it is only a suggestion and can be edited.
- **Discounts.** Sales order and sales invoice lines have a *discount %* (0-100). A sales invoice also has an *invoice discount* amount, deducted from the sum of the discounted lines (it cannot exceed it). The invoice total, receivable, credit-limit check, statements and ageing all use the net amount. Posting: Dr Receivables (net) + Dr *Sales discounts given* (new contra-revenue account 4250, created automatically on older databases) / Cr Sales revenue at the undiscounted value, so the books show how much was given away. Delivery notes carry no discount (they only move goods). A return linked to a discounted invoice is pre-filled at the net unit price.
- **Vehicle load.** Items have a *weight (kg per unit)*. A trip shows the total weight of its live (not cancelled) deliveries against the vehicle's capacity and warns when it is exceeded, and when some goods have no recorded weight. It is a warning only; nothing is blocked.

## Returns
- **Purchase returns** (`PRT-` numbers): confirming takes the goods out of the warehouse at its average cost and lowers what we owe the supplier (Dr Payables at the return price, Cr Inventory at cost, difference to *Goods received not invoiced*). Cancelling reverses both. Supplier statement/balances show them; the Payables reconciliation counts them.
- **Linking to an invoice** (purchase returns -> purchase invoice, sales returns -> sales invoice; optional): the invoice must be confirmed, same supplier/customer and currency, items must be on it, and per item the returned quantity cannot exceed *invoiced - already returned (confirmed)*. A confirmed linked return reduces the invoice's **outstanding** amount (open invoices, ageing, allocation cap, revaluation). An invoice with confirmed linked returns cannot be cancelled until they are cancelled. From a confirmed invoice screen, *Create return* pre-fills a return.

## Search selects (large lists)
Item, supplier, customer and warehouse pickers (document headers and lines, transfers, adjustments, statements, stock filters) are **server-side search selects**: type a code or name and the API returns the best 20 matches, so lists with thousands of records work. Records fetched this way are added to the client cache so their labels display correctly; list pages fetch labels for any id outside the cached first 500. Master-data lists (currencies, units, drivers, vehicles, accounts) are still plain dropdowns because they stay small.

## Deployment (Windows Server)
See `deploy/README.md`: a release package (`deploy/build-release.ps1`) and install scripts for IIS + SQL Server (ERP) and IIS reverse proxy + Node + PostgreSQL (website), with HTTPS, upgrades and nightly backups.

## Driver app (mobile)
Delivery drivers use a mobile app (`erp/mobile`, Flutter) instead of the web ERP.
- **Accounts:** create a user with the **Driver** role and pick the **driver record** it belongs to (Users screen; one account per driver). A driver-only account is locked to the driver API: every other `/api` call returns 403 and the web login refuses it.
- **Sign-in:** `POST /api/v1/auth/token` (email + password, same rate limit as the web login) returns a bearer token valid `Jwt:MobileExpiryMinutes` (default 12 h). Bearer calls carry no cookie, so CSRF does not apply.
- **Driver API** (`/api/v1/driver`, always scoped to the caller's own trips, otherwise 404): `GET trips` (planned / in transit / last 7 days), `GET trips/{id}` (stops with customer name, phone, address, delivery lines, collected money, proofs; load vs vehicle capacity), `POST trips/{id}/start|complete`, `PUT trips/{id}/stops/{stopId}` (Delivered / Failed; Failed needs a reason; optional GPS position), `POST stops/{stopId}/collections` (money collected: creates a **Draft customer receipt** referencing the trip, which the accountant confirms in the normal receipts screen), `POST stops/{stopId}/proof` (multipart `kind` Signature|Photo + `file`; PNG/JPEG only, checked by content, max 3 MB, 5 per stop) and `GET proofs/{id}`. Completing a trip still cancels the deliveries of failed stops and puts the goods back in stock.
- **Proof files** are stored under `Storage:ProofsPath` (default `App_Data/proofs`, outside `wwwroot`, never served directly); ERP staff open them from the Trips screen (`/api/v1/stopproofs/{id}`, needs sales-view rights).
- **Development:** CORS for `localhost` origins is enabled only in Development (for the Flutter web preview).

## Look and feel
The ERP follows the website's design (`frontend/css/style.css`): cream background, brand red `#a8202c`, olive `#718c3f`, Cairo, 20px cards, pill buttons, a dark ink sidebar with the logo. All colors, radii and shadows are CSS variables at the top of `web/src/styles.scss` (light and dark sets), so a palette change is made in one place.

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
