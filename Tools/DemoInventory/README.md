# DemoInventory

Development-only offline synthetic inventory importer. It is never registered as
an HTTP endpoint or run automatically at application startup. No database or
imported dataset is distributed with this repository.

## Preview

Read the root README before configuring a fresh local environment. The CLI uses
the WebAPI project's separate User Secrets store; do not pass credentials as args.

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project Tools/DemoInventory --configuration Debug -- --dry-run
```

No arguments also selects dry-run. The target guards require local
InventoryManagement_Dev, loopback port 14331, healthy inventory_sqlserver, Compose
project inventorymanagement and volume inventorymanagement_inventory_sql_data at
/var/opt/mssql. Preview accepts exactly the known seven- or eight-migration history;
actual import requires all eight migrations including AddDemoSeedLedger.
Use `docker compose --project-name inventorymanagement` only for a new disposable
local environment. Do not reuse an existing installation to try this portfolio.

The plan aims for 40 total products, 5 total warehouses and 180 movements including
existing/inactive rows. Added counts vary with the starting database. No existing
product is used as a stock source. Inactive warehouses are not reactivated. Unique
barcode strings begin with 000-DEMO-J- and preserve leading zeros. Product names,
locations and example TRY prices are synthetic, with no GS1 allocation claim.

## Explicit local import

Import is a separate operator action. Verify a current full backup, target,
migration state, maintenance window, and exact printed plan hash first:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project Tools/DemoInventory --configuration Debug -- --apply
```

A local interactive terminal is mandatory. The operator must type the full
`APPLY inventory-demo-j-v1 <printed-plan-SHA256>` confirmation. Redirected
stdin/stdout/stderr are rejected. The CLI does not take backups, apply migrations,
restore databases or automatically retry.

A SERIALIZABLE transaction covers every insertion and the DemoSeedRuns ledger.
A zero-wait transaction-owned SQL application lock rejects competing importers.
The dataset key and deterministic product/warehouse/inventory/movement IDs provide
persistent deduplication independent of barcodes. Existing rows are hashed and
checked before commit. The existing stock service, validation, concurrency token
and audit interceptor are reused. Timestamps are actual UTC saves; actor IDs stay
null and notes identify the synthetic source. Pending orders are not converted
into stock operations. Failures roll back the tool's transaction and stop.

Reapplying the same unmodified dataset adds nothing; modified or orphaned demo
records cause a safe stop. This tool is not a repair, reconciliation or cleanup tool.

## Tests

See the root README. Normal tests use isolated SQLite without a real SQL database.
One opt-in SQL case deliberately creates/migrates/drops a separate test database;
review its source and use an explicitly isolated environment before enabling it.
No database, backup, real user, or operational run report is bundled here.
