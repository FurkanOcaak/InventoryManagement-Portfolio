# Inventory Management ERP

## Project Overview

A portfolio inventory management application with an ASP.NET Core API and a React
client. It covers product and warehouse management, auditable stock operations,
role-based access, and a customer catalog, cart, and order workflow.

This repository contains source code and tests, not a running hosted service.
Real databases, database backups, user accounts, credentials, and customer records
are not included. Database creation and account provisioning are separate tasks.

## Features

- Product barcodes stored as unique strings, preserving leading zeros.
- Products, customers, and warehouses with validation and soft-deactivation rules.
- Stock-in/out operations, non-negative balances, movement history, and low-stock alerts.
- Audit timestamps and actor attribution, with optimistic inventory concurrency control.
- Customer-linked accounts with ownership checks for catalog, cart, and order APIs.
- Server-priced checkout with idempotency keys and order line snapshots.
- A guarded offline CLI for repeatable, synthetic demo inventory.

Orders are created in **Pending** status. Fulfillment, payment processing, and
order-triggered stock deduction are not implemented. A submitted order does not
reserve stock; order quantities must not be interpreted as fulfilled quantities.

## Technology Stack

| Area | Technology |
|---|---|
| Backend | ASP.NET Core, .NET 10, C# |
| Persistence | Entity Framework Core 10, SQL Server 2022, Docker Compose |
| Authentication | ASP.NET Core Identity, cookies, antiforgery protection |
| Frontend | React, TypeScript, Vite, React Router |
| Validation | FluentValidation |
| Testing | xUnit, ASP.NET Core TestServer, SQLite, Node test runner, isolated Chrome/CDP |

Use the committed project files and package lockfile for exact dependency versions.

## Architecture

```text
Frontend/       React pages, role navigation, same-origin API client, CSRF handling
WebAPI/         HTTP endpoints, cookie authentication, authorization, error handling
Business/       Use cases, validation, interfaces, and domain-facing contracts
DataAccess/     EF Core context, entity configurations, repositories, Identity
Entities/       Product, warehouse, inventory, movement, customer, cart/order models
Tools/          Explicit local provisioning and demo maintenance CLIs
Tests/          Unit, model, security, and isolated API integration tests
```

Repositories implement Business interfaces. The API composes services through
dependency injection. Stock operations use concurrency tokens and transactions;
checkout and demo import have their own transaction/idempotency boundaries.

## Authentication and Authorization

The API uses Identity cookies, not bearer tokens stored in browser storage.
Cookies are HttpOnly; state-changing requests require the `X-CSRF-TOKEN` header.
The frontend fetches antiforgery state and handles 401/403 without silently
replaying failed writes. Login rate limiting and account lockout are configured.

| Role | Intended access |
|---|---|
| Admin | Product/warehouse/customer management and administrative order views |
| WarehouseStaff | Product/warehouse/stock reads and stock-in/out operations |
| Customer | Catalog, own cart, and own orders through an active customer link |
| Viewer | Read-only product, warehouse, and stock views |

Customer access requires an active linked Customer record. There is no public
registration endpoint or automatic account creation. Cookie tickets are stored
in server memory; process restarts invalidate sessions, and a shared ticket store
is needed for a multi-instance deployment.

## Database and Migrations

Eight EF Core migrations are included, with matching Designer files and the model
snapshot. They cover customers; products/warehouses; inventory/movements; Identity;
barcode renaming; the user/customer relationship; shopping/orders; and demo ledger.
No database is bundled, and migrations are not automatically applied at API startup.

The maintenance CLIs intentionally require fixed local target names:
`InventoryManagement_Dev`, `inventory_sqlserver`, Compose project
`inventorymanagement`, loopback port `14331`, and volume
`inventorymanagement_inventory_sql_data`. These names are guard conditions, not
credentials. Do not point a fresh portfolio checkout at an existing installation.

**Account provisioning limitation:** `Tools/AdminProvisioning` requires exactly
the first four migrations and no users; `Tools/AccountProvisioning` requires
exactly the first six migrations and an existing Admin. They reject a fully
migrated eight-migration database. The current user model also contains fields
introduced after the initial bootstrap stage. This snapshot therefore does not
provide a supported one-command fresh-database/account setup. Do not downgrade,
reset, or bypass guards to satisfy them. A compatible account bootstrap procedure
needs a separate reviewed change before end-to-end sign-in on a fresh install.

## Local Development

### Prerequisites

- .NET 10 SDK and the EF tool version in `dotnet-tools.json`.
- Node.js compatible with the locked Vite release and npm.
- Docker with a SQL Server-compatible local environment, if running the real API.
- Optional Chrome/Chromium for the isolated browser scenarios.

The tests below do not require your real SQL database or accounts.

### Restore and configure a separate development environment

```powershell
dotnet restore InventoryManagement.slnx
dotnet tool restore --tool-manifest dotnet-tools.json
```

For a new, disposable local SQL environment only, copy `.env.example` to an
untracked `.env` and replace the placeholder with your own strong local password.
Do not reuse an existing container/volume or commit the resulting file.

```powershell
Copy-Item .env.example .env
# Edit .env locally; the placeholder must be replaced before starting SQL Server.
docker compose --project-name inventorymanagement up -d
```

The API reads `ConnectionStrings:InventoryDatabase`. Configure it privately in the
WebAPI User Secrets store, not in a tracked appsettings file. Its connection shape
is `Server=127.0.0.1,14331;Database=InventoryManagement_Dev;User Id=sa;Password=<YOUR_LOCAL_PASSWORD>;TrustServerCertificate=True`.
The password placeholder is not a credential. One way to avoid putting the real
connection string in command arguments or shell history is masked input via stdin:

```powershell
$secureConnection = Read-Host 'Your local SQL connection string' -AsSecureString
$connectionText = [Net.NetworkCredential]::new('', $secureConnection).Password
try {
    @{ 'ConnectionStrings:InventoryDatabase' = $connectionText } |
        ConvertTo-Json -Compress | dotnet user-secrets set --project WebAPI
} finally {
    $connectionText = $null
    $secureConnection.Dispose()
}
```

This copy uses the separate, non-secret identifier
`InventoryManagement-Portfolio-Development`. No secret store is shipped. User
Secrets are a local development convenience, not a production secret manager.

After reviewing the target, schema creation for a **new local database** is a
separate operator action:

```powershell
dotnet ef database update --project DataAccess --startup-project WebAPI
```

This creates the schema, not accounts. Read the provisioning limitation above
before attempting to sign in; no default username or password is provided.
Once compatible accounts have been provisioned through a reviewed process:

```powershell
dotnet run --project WebAPI --launch-profile http
# In a second terminal:
cd Frontend
npm ci
npm run dev
```

The development API uses `http://localhost:5098`. Vite serves the client on port
5173 and proxies `/api` to the API, preserving the same-origin cookie/CSRF flow.
HTTP and local development cookies are for development; production hosting,
HTTPS, persistence, and operational secret management require separate setup.

## Testing

Run from this repository, never by connecting tests to an existing installation:

```powershell
dotnet restore InventoryManagement.slnx
dotnet build InventoryManagement.slnx -c Release
dotnet test Tests/Tests.csproj -c Release --no-restore

cd Frontend
npm ci
npm run build
npm run lint
node --test tests/apiFetch.test.mjs tests/roleRouting.test.mjs tests/commerce.test.mjs
```

The normal backend suite has 476 discovered cases: 475 runnable without SQL Server
and one opt-in SQL integration case that is skipped by default. API fixtures use
isolated SQLite and ephemeral test authentication keys. Frontend unit tests have
27 cases. Counts describe this snapshot, not a promise about later changes.

For the 14 isolated Chrome scenarios, start Vite on port 5173, then run from
Frontend:

```powershell
node tests/browser-smoke.mjs
node tests/shopping-browser.mjs
```

Every API request in those browsers is intercepted by fixtures. The scenarios do
not create real users or orders. Windows has a default Chrome path; on other
supported installations set `CHROME_PATH` to your local Chrome/Chromium binary.

The opt-in backend SQL test creates/migrates/drops a separate random test database.
It is intentionally excluded from the normal portable test command; see its source
and use only a deliberately configured disposable SQL environment if enabling it.

## Demo Inventory

[DemoInventory](Tools/DemoInventory/README.md) is a Development-only offline CLI.
Its default mode is a read-only preview:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project Tools/DemoInventory --configuration Debug -- --dry-run
```

It plans toward approximately 40 total products, 5 warehouses, and 180 movements,
including existing and inactive records. Added counts depend on existing data.
Products, prices, barcodes, and locations are synthetic; barcode strings are
explicitly marked as demo data and do not claim a GS1 allocation.

A real import requires a separately verified backup, correct local target,
maintenance window, and explicit interactive confirmation of the exact plan hash.
A serializable transaction, persistent dataset ledger, stable movement IDs, and
SQL application lock prevent partial or duplicate imports. Existing rows are
hashed and checked before commit. Actor IDs remain null for synthetic operations;
no real user is impersonated. The tool never turns pending orders into movements.

No demo database, imported dataset, live account, or backup is included here.

## Known Limitations

- Orders remain Pending; fulfillment, payment, and automatic stock deduction are absent.
- The historical account provisioning guards need review for a full eight-migration install.
- Session tickets live in memory; multi-instance production operation is not configured.
- Maintenance CLIs are intentionally tied to fixed local Development target names.
- A modified demo dataset causes safe rejection on reapply; it is not a repair/cleanup tool.
- The optional SQL integration test is skipped unless explicitly enabled in an isolated environment.
- Existing EF/xUnit deprecation/style warnings and one browser-fixture lint warning may appear.
- The locked Vite development toolchain currently reports five high-severity npm audit
  findings rooted in [GHSA-fc5q-39xc-vjpf](https://github.com/advisories/GHSA-fc5q-39xc-vjpf)
  (picomatch ReDoS). At this review, the upstream advisory lists no patched release.
  Dependency versions and audit warnings have not been suppressed or force-fixed;
  review this unresolved issue before public release or exposing development tooling.
- No open-source license has been selected; no LICENSE file is included.
