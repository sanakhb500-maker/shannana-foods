# Shannana ERP

Internal ERP for Shannana Foods: master data, then inventory, purchasing, sales/distribution and accounting.
Standalone from the public website (`backend/` + `frontend/`): own database, own users.

**Stack:** ASP.NET Core (.NET 10) Web API + Angular, SQL Server via EF Core. UI in Arabic (RTL) and English.

## Status
| Phase | Scope | State |
|---|---|---|
| 1 | Foundation + master data (auth, roles, items, units, warehouses, suppliers, customers, drivers, vehicles, currencies, rates, users) | done |
| 2 | Inventory and warehouses | planned |
| 3 | Purchasing | planned |
| 4 | Sales and distribution | planned |
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
| `Seed:LocalCurrencyCode` | local (base) currency, default `JOD`; **fixed after the first run** |

For production set these through environment variables (`Jwt__Key`, `ConnectionStrings__Default`, ...) or user-secrets, never in committed files.

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
