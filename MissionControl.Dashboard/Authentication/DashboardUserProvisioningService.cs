using Microsoft.AspNetCore.Identity;

namespace MissionControl.Dashboard.Authentication;

public sealed class DashboardUserProvisioningService(
    IDashboardUserStore userStore,
    IPasswordHasher<DashboardUser> passwordHasher)
{
    private const int MinimumPasswordLength = 12;

    public async Task<DashboardUser> CreateAsync(
        string username,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (password.Length < MinimumPasswordLength)
        {
            throw new ArgumentException(
                $"Password must be at least " +
                $"{MinimumPasswordLength} characters.",
                nameof(password));
        }

        string trimmedUsername = username.Trim();
        string normalizedUsername = DashboardUsernameNormalizer.Normalize(trimmedUsername);

        DashboardUser? existingUser =
            await userStore
                .FindByNormalizedUsernameAsync(
                    normalizedUsername,
                    cancellationToken);

        if (existingUser is not null)
        {
            throw new InvalidOperationException(
                $"Dashboard user '{trimmedUsername}' " +
                "already exists.");
        }

        DateTimeOffset createdAtUtc = DateTimeOffset.UtcNow;
        string securityStamp = Guid.NewGuid().ToString("N");

        var passwordHashSubject =
            new DashboardUser(
                Id: 0,
                Username: trimmedUsername,
                NormalizedUsername: normalizedUsername,
                DisplayName: displayName.Trim(),
                PasswordHash: string.Empty,
                IsEnabled: true,
                FailedLoginCount: 0,
                LockoutEndUtc: null,
                SecurityStamp: securityStamp,
                CreatedAtUtc: createdAtUtc,
                UpdatedAtUtc: createdAtUtc);

        string passwordHash = passwordHasher.HashPassword(passwordHashSubject, password);

        var newUser =
            new NewDashboardUser(
                Username: trimmedUsername,
                NormalizedUsername: normalizedUsername,
                DisplayName: displayName.Trim(),
                PasswordHash: passwordHash,
                SecurityStamp: securityStamp,
                CreatedAtUtc: createdAtUtc);

        return await userStore.CreateAsync(newUser, cancellationToken);
    }

    public async Task ResetPasswordAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (password.Length < MinimumPasswordLength)
        {
            throw new ArgumentException(
                $"Password must be at least " +
                $"{MinimumPasswordLength} characters.",
                nameof(password));
        }

        string normalizedUsername = DashboardUsernameNormalizer.Normalize(username);

        DashboardUser? user =
            await userStore
                .FindByNormalizedUsernameAsync(normalizedUsername, cancellationToken);

        if (user is null)
        {
            throw new InvalidOperationException(
                $"Dashboard user '{username.Trim()}' " +
                "does not exist.");
        }

        string passwordHash = passwordHasher.HashPassword(user, password);
        string securityStamp = Guid.NewGuid().ToString("N");

        await userStore.ResetPasswordAsync(
            user.Id,
            passwordHash,
            securityStamp,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }
}