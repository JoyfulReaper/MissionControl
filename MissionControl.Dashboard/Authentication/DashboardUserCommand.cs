using System.Text;

namespace MissionControl.Dashboard.Authentication;

public static class DashboardUserCommand
{
    public static async Task<int?> TryRunAsync(
        string[] args,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);

        if (args.Length == 0 ||
            !string.Equals(
                args[0],
                "users",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (args.Length < 2)
        {
            WriteUsage();
            return 2;
        }

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("An interactive terminal is required.");
            return 2;
        }

        return args[1].ToLowerInvariant() switch
        {
            "create" =>
                await RunCreateAsync(
                    args,
                    services,
                    cancellationToken),

            "reset-password" =>
                await RunResetPasswordAsync(
                    args,
                    services,
                    cancellationToken),

            _ => InvalidUsage()
        };
    }

    private static async Task<int> RunCreateAsync(
        string[] args,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (args.Length is < 3 or > 4)
        {
            WriteUsage();
            return 2;
        }

        string username = args[2];

        string displayName =
            args.Length == 4
                ? args[3]
                : username;

        string? password = ReadConfirmedPassword();

        if (password is null)
        {
            return 1;
        }

        try
        {
            using IServiceScope scope = services.CreateScope();

            var provisioningService =
                scope.ServiceProvider
                    .GetRequiredService<
                        DashboardUserProvisioningService>();

            DashboardUser user =
                await provisioningService.CreateAsync(
                    username,
                    displayName,
                    password,
                    cancellationToken);

            Console.WriteLine($"Created dashboard user '{user.Username}'.");

            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);

            return 1;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);

            return 1;
        }
    }

    private static async Task<int>
        RunResetPasswordAsync(
            string[] args,
            IServiceProvider services,
            CancellationToken cancellationToken)
    {
        if (args.Length != 3)
        {
            WriteUsage();
            return 2;
        }

        string username = args[2];
        string? password = ReadConfirmedPassword();

        if (password is null)
        {
            return 1;
        }

        try
        {
            using IServiceScope scope = services.CreateScope();

            var provisioningService =
                scope.ServiceProvider
                    .GetRequiredService<DashboardUserProvisioningService>();

            await provisioningService.ResetPasswordAsync(
                username,
                password,
                cancellationToken);

            Console.WriteLine(
                $"Reset password for dashboard user " +
                $"'{username}'.");

            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);

            return 1;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);

            return 1;
        }
    }

    private static string? ReadConfirmedPassword()
    {
        Console.Write("Password: ");
        string password = ReadSecret();
        Console.WriteLine();

        Console.Write("Confirm password: ");
        string confirmation = ReadSecret();
        Console.WriteLine();

        if (string.Equals(
                password,
                confirmation,
                StringComparison.Ordinal))
        {
            return password;
        }

        Console.Error.WriteLine("Passwords do not match.");

        return null;
    }

    private static string ReadSecret()
    {
        var value = new StringBuilder();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                return value.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                {
                    value.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
            }
        }
    }

    private static int InvalidUsage()
    {
        WriteUsage();
        return 2;
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  users create <username> [display-name]");
        Console.Error.WriteLine("  users reset-password <username>");
    }
}