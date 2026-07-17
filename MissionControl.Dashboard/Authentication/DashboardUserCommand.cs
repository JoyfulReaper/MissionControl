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

        if (args.Length is < 3 or > 4 ||
            !string.Equals(
                args[1],
                "create",
                StringComparison.OrdinalIgnoreCase))
        {
            WriteUsage();
            return 2;
        }

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine(
                "An interactive terminal is required.");

            return 2;
        }

        string username = args[2];

        string displayName =
            args.Length == 4
                ? args[3]
                : username;

        Console.Write("Password: ");
        string password = ReadSecret();
        Console.WriteLine();

        Console.Write("Confirm password: ");
        string confirmation = ReadSecret();
        Console.WriteLine();

        if (!string.Equals(
                password,
                confirmation,
                StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Passwords do not match.");

            return 1;
        }

        try
        {
            using IServiceScope scope =
                services.CreateScope();

            DashboardUserProvisioningService provisioningService =
                scope.ServiceProvider
                    .GetRequiredService<
                        DashboardUserProvisioningService>();

            DashboardUser user =
                await provisioningService.CreateAsync(
                    username,
                    displayName,
                    password,
                    cancellationToken);

            Console.WriteLine(
                $"Created dashboard user '{user.Username}'.");

            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(
                exception.Message);

            return 1;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(
                exception.Message);

            return 1;
        }
    }

    private static string ReadSecret()
    {
        var value = new StringBuilder();

        while (true)
        {
            ConsoleKeyInfo key =
                Console.ReadKey(intercept: true);

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

    private static void WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage:");

        Console.Error.WriteLine(
            "  users create <username> [display-name]");
    }
}