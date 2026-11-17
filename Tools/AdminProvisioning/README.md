# First Admin Provisioning

Local Development-only operator CLI. It accepts credentials through an interactive
console, masks and confirms passwords, and requires explicit final confirmation.
It is never registered as an automatic API startup task or public endpoint.

The target guard requires exactly the first four migrations, the known local SQL
container/volume/database, and no existing users. The latest application model has
eight migrations and later user fields. Consequently this snapshot does not offer
a supported fresh-install account bootstrap. The guard rejects the latest schema;
do not downgrade, reset or bypass it. Resolve the bootstrap/version compatibility
in a separately reviewed change before using it to initialize a new installation.

The read-only command is:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project Tools/AdminProvisioning -- --check-target
```

The historical creation flag is --create-first-admin. It is documented here as the
existing interface, not as a working one-command setup for the current schema.
No real credentials, user records or default administrator password are included.
