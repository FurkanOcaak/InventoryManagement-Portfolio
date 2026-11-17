using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataAccess.Persistence;
using Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace InventoryManagement.DemoInventory;

// Hash every persisted scalar, including audit, Identity and commerce values.
// Neither raw rows nor credentials are included in reports or the demo plan.
public sealed record DataSnapshot(Dictionary<string, Dictionary<string, string>> Tables)
{
    public static async Task<DataSnapshot> CaptureAsync(InventoryDbContext db, CancellationToken ct = default)
    {
        var tables = new Dictionary<string, Dictionary<string, string>>();
        bool close = db.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        if (close) await db.Database.OpenConnectionAsync(ct);
        try
        {
            foreach (var entity in db.Model.GetEntityTypes().Where(e => e.ClrType != typeof(DemoSeedRun)))
            {
                string table = entity.GetTableName()!;
                if (tables.ContainsKey(table)) continue;
                var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
                var keys = entity.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName(store)!).ToArray();
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
                command.CommandText = $"SELECT * FROM [{table.Replace("]", "]]", StringComparison.Ordinal)}]";
                await using var reader = await command.ExecuteReaderAsync(ct);
                var rows = new Dictionary<string, string>();
                while (await reader.ReadAsync(ct))
                {
                    string key = JsonSerializer.Serialize(keys.Select(k => reader[k]).ToArray());
                    var values = Enumerable.Range(0, reader.FieldCount)
                        .Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray();
                    rows.Add(key, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)))));
                }
                tables.Add(table, rows);
            }
            return new(tables);
        }
        finally { if (close) await db.Database.CloseConnectionAsync(); }
    }

    public void VerifyPreserved(DataSnapshot after, DemoPlan? additions = null)
    {
        foreach (var (table, rows) in Tables)
        {
            if (!after.Tables.TryGetValue(table, out var current)
                || rows.Any(row => !current.TryGetValue(row.Key, out var hash) || hash != row.Value))
                throw new InvalidOperationException("Existing data fingerprint changed. Transaction must stop.");
            int delta = additions is null ? 0 : table switch
            {
                "Products" => additions.Products.Length,
                "Warehouses" => additions.Warehouses.Length,
                "Inventories" => additions.Products.Length,
                "StockMovements" => additions.Movements.Length,
                _ => 0
            };
            if (current.Count != rows.Count + delta)
                throw new InvalidOperationException("Unexpected table count change. Transaction must stop.");
        }
    }
}
