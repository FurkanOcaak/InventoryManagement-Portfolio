using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebAPI.Controllers;
using WebAPI.Infrastructure.Provisioning;

namespace InventoryManagement.AdminProvisioning;

public static class ProvisioningCommand
{
    public static bool IsExplicitInvocation(string[] args)
        => args.Length == 1 && args[0] == "--create-first-admin";
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var checkOnly = args.Length == 1 && args[0] == "--check-target";
        if (!ProvisioningCommand.IsExplicitInvocation(args) && !checkOnly)
        {
            Console.WriteLine("Usage: DOTNET_ENVIRONMENT=Development; AdminProvisioning --create-first-admin");
            Console.WriteLine("Read-only preflight: AdminProvisioning --check-target");
            Console.WriteLine("Credentials are accepted interactively only; never pass them as arguments.");
            return 2;
        }
        if (!checkOnly && (Console.IsInputRedirected || Console.IsOutputRedirected))
        {
            Console.Error.WriteLine("A local interactive console is required.");
            return 2;
        }

        var stage = "HostConfiguration";
        try
        {
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            if (!builder.Environment.IsDevelopment())
            {
                Console.Error.WriteLine("Development environment is required. No changes made.");
                return 2;
            }
            // Reuse the WebAPI secret store and restore environment-variable precedence.
            builder.Configuration.AddUserSecrets(typeof(AuthController).Assembly, optional: false)
                .AddEnvironmentVariables();
            stage = "ServiceRegistration";
            builder.Logging.ClearProviders();
            builder.Services.AddProvisioningServices(builder.Configuration, builder.Environment);
            stage = "HostBuild";
            using var host = builder.Build();
            using var scope = host.Services.CreateScope();
            stage = "TargetPreflight";
            var guard = scope.ServiceProvider.GetRequiredService<ProvisioningTargetGuard>();
            if (!await guard.ValidateAsync())
            {
                Console.Error.WriteLine("Target/container/migrations/schema check failed, or users already exist. No changes made.");
                return 3;
            }

            Console.WriteLine("Verified: local InventoryManagement_Dev, 4 migrations, 7 Identity tables, 0 users.");
            if (checkOnly) return 0;
            Console.WriteLine("Scope: first user, Admin role if absent, and one role membership. Business data is unchanged.");
            stage = "InteractiveInput";
            Console.Write("Admin email: ");
            var email = Console.ReadLine()?.Trim();
            Console.Write("New password (20+ characters; Esc cancels): ");
            var password = ReadMaskedPassword();
            if (password is null) return 4;
            Console.Write("Confirm password: ");
            var confirmation = ReadMaskedPassword();
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Passwords do not match. No changes made.");
                return 4;
            }
            var provisioner = scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>();
            var errors = await provisioner.ValidateInputAsync(email, password);
            if (errors.Count > 0)
            {
                Console.Error.WriteLine("Validation failed: " + string.Join(", ", errors));
                return 4;
            }
            Console.WriteLine("Save the password in your password manager before proceeding.");
            Console.Write("Final confirmation: type CREATE FIRST ADMIN to write these Identity records: ");
            if (Console.ReadLine() != "CREATE FIRST ADMIN")
            {
                Console.WriteLine("Cancelled. No changes made.");
                return 4;
            }
            if (!await guard.ValidateAsync())
            {
                Console.Error.WriteLine("Preconditions changed. No changes made.");
                return 3;
            }
            stage = "Provisioning";
            var result = await provisioner.ProvisionAsync(email, password, confirmed: true);
            password = null;
            confirmation = null;
            email = null;
            if (result.Status != ProvisioningStatus.Created)
            {
                Console.Error.WriteLine("Provisioning stopped: " + result.Status + ". Check database state before retrying.");
                return 5;
            }
            Console.WriteLine("SUCCESS: first Admin created; role, password hashing and sign-in eligibility verified.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Operation stopped at {stage} ({exception.GetType().Name}). No automatic retry. Check database state before retrying.");
            if (exception is AggregateException aggregate)
            {
                foreach (var type in aggregate.Flatten().InnerExceptions
                    .SelectMany(error => Regex.Matches(error.Message,
                        "Unable to resolve service for type '([A-Za-z0-9_.`+\\[\\], ]+)'")
                        .Select(match => match.Groups[1].Value)).Distinct())
                    Console.Error.WriteLine("Missing service type: " + type);
            }
            return 6;
        }
        finally
        {
            if (!checkOnly)
            {
                Console.Write("Press Enter to close this local console: ");
                Console.ReadLine();
            }
        }
    }

    private static string? ReadMaskedPassword()
    {
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape) { Console.WriteLine(); text.Clear(); return null; }
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                var value = text.ToString();
                text.Clear();
                return value;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0) { text.Length--; Console.Write("\b \b"); }
            }
            else if (!char.IsControl(key.KeyChar) && text.Length < 1024)
            {
                text.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }
}
