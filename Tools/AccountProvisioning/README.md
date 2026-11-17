# AccountProvisioning

Offline local Development tool for one WarehouseStaff account or one Customer
account linked to an operator-selected active, unlinked Customer record. It cannot
create Admins or modify existing accounts. It is not an HTTP registration endpoint.

## Version limitation

The guard requires exactly the first six migrations, an existing Admin and the
expected local Docker/SQL target. The current application has eight migrations;
therefore this tool intentionally refuses that fully migrated schema. It does
not currently provide a supported account-setup path for a fresh portfolio install.
Do not downgrade/reset databases or disable the guard. A compatible bootstrap and
provisioning workflow needs a separate reviewed change. See the root README.

## Existing command interface

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project Tools/AccountProvisioning -- --check-target
```

The check reads only. For a separately reviewed compatible historical environment,
the command flags are `--create-warehouse-staff` and `--create-customer`. Creation
requires an interactive local console, an email and a masked/confirmed password
of at least 20 characters satisfying Identity policy. Credentials must not appear
in arguments, chat, files or logs. The operator selects a Customer; no automatic
selection is made. Final typed confirmation is CREATE WarehouseStaff or CREATE
Customer. One transaction protects user/role/link creation; existing accounts stay
unchanged. No automatic retries or raw SQL/credential diagnostics are emitted.

The tool is for trusted local operators with local DB/Secrets access; it does not
verify a browser Admin session. Public registration and default credentials are
not provided. No real accounts or historical operational results are distributed.
