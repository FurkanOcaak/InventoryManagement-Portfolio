using System.Text;
using Business.Common.Security;
using DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebAPI.Controllers;

namespace InventoryManagement.AccountProvisioning;

public static class AccountProvisioningCommand
{
    public static string? ParseRole(string[] args) => args.Length == 1 ? args[0] switch
    {
        "--create-warehouse-staff" => ApplicationRoles.WarehouseStaff,
        "--create-customer" => ApplicationRoles.Customer,
        _ => null
    } : null;

    public static string SafeLabel(string value)
        => string.Concat(value.Where(character => !char.IsControl(character)).Take(80));
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var role = AccountProvisioningCommand.ParseRole(args);
        var checkOnly = args.Length == 1 && args[0] == "--check-target";
        if (role is null && !checkOnly)
        {
            Console.WriteLine("Usage: AccountProvisioning --create-warehouse-staff | --create-customer | --check-target");
            Console.WriteLine("Development only. Credentials are interactive; never supply them as arguments.");
            return 2;
        }
        if (!checkOnly && (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected))
        {
            Console.Error.WriteLine("An interactive local console is required.");
            return 2;
        }
        try
        {
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            if (!builder.Environment.IsDevelopment())
            {
                Console.Error.WriteLine("Development environment required. No changes made.");
                return 2;
            }
            builder.Configuration.AddUserSecrets(typeof(AuthController).Assembly, optional: false).AddEnvironmentVariables();
            builder.Logging.ClearProviders();
            builder.Services.AddAccountProvisioningServices(builder.Configuration, builder.Environment);
            using var host = builder.Build(); // Never start an HTTP server.
            using var scope = host.Services.CreateScope();
            var services = scope.ServiceProvider;
            var guard = services.GetRequiredService<IAccountProvisioningTarget>();
            if (!await guard.ValidateAsync())
            {
                Console.Error.WriteLine("Target/container/volume/schema/Admin verification failed. No changes made.");
                return 3;
            }
            Console.WriteLine("Verified: local InventoryManagement_Dev, 6 migrations, expected Identity schema and existing Admin.");
            if (checkOnly) return 0;
            Guid? customerId = null;
            if (role == ApplicationRoles.Customer)
            {
                var db = services.GetRequiredService<InventoryDbContext>();
                var customers = await db.Customers.AsNoTracking()
                    .Where(customer => !db.Users.Any(user => user.CustomerId == customer.Id))
                    .OrderBy(customer => customer.Code)
                    .Select(customer => new { customer.Id, customer.Code, customer.Name }).ToListAsync();
                if (customers.Count == 0) { Console.WriteLine("No active, unlinked customer available."); return 4; }
                for (var index = 0; index < customers.Count; index++)
                    Console.WriteLine($"{index + 1}. {AccountProvisioningCommand.SafeLabel(customers[index].Code)} — {AccountProvisioningCommand.SafeLabel(customers[index].Name)}");
                Console.Write("Choose the verified existing customer number (blank cancels): ");
                if (!int.TryParse(Console.ReadLine(), out var choice) || choice < 1 || choice > customers.Count) return 4;
                customerId = customers[choice - 1].Id;
            }
            Console.Write("New account email: ");
            var email = Console.ReadLine()?.Trim();
            Console.Write("New password (20+ characters, Esc cancels): ");
            var password = ReadMaskedPassword();
            if (password is null) return 4;
            Console.Write("Confirm password: ");
            var confirmation = ReadMaskedPassword();
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            { Console.Error.WriteLine("Passwords do not match. No changes made."); return 4; }
            var provisioner = services.GetRequiredService<AccountProvisioner>();
            if (!await provisioner.IsValidInputAsync(email, password, role!, customerId))
            { Console.Error.WriteLine("Invalid email, role, customer or password policy. No changes made."); return 4; }
            Console.WriteLine($"Scope: one new {role} account and membership; create only that role if absent. Existing accounts remain unchanged.");
            Console.WriteLine("Save the password in your password manager before proceeding.");
            Console.Write($"Type CREATE {role} to confirm the database write: ");
            if (Console.ReadLine() != $"CREATE {role}") { Console.WriteLine("Cancelled. No changes made."); return 4; }
            var result = await provisioner.ProvisionAsync(email, password, role!, customerId, confirmed: true);
            password = null; confirmation = null; email = null;
            if (result.Status != AccountProvisioningStatus.Created)
            { Console.Error.WriteLine($"Stopped: {result.Status}. No automatic retry; check database state."); return 5; }
            Console.WriteLine($"SUCCESS: one {role} account created and verified. Existing Admin unchanged.");
            return 0;
        }
        catch
        {
            Console.Error.WriteLine("Operation stopped. No automatic retry. Check database state before retrying.");
            return 6;
        }
    }

    private static string? ReadMaskedPassword()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape) { Console.WriteLine(); buffer.Clear(); return null; }
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); var value = buffer.ToString(); buffer.Clear(); return value; }
            if (key.Key == ConsoleKey.Backspace)
            { if (buffer.Length > 0) { buffer.Length--; Console.Write("\b \b"); } }
            else if (!char.IsControl(key.KeyChar))
            {
                if (buffer.Length == 1024) { Console.WriteLine(); buffer.Clear(); return null; }
                buffer.Append(key.KeyChar); Console.Write('*');
            }
        }
    }
}
