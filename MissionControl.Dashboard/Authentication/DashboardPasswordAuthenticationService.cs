using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace MissionControl.Dashboard.Authentication;

public sealed class DashboardPasswordAuthenticationService
{
    private readonly IDashboardUserStore _userStore;
    private readonly IPasswordHasher<DashboardUser> _passwordHasher;
    private readonly DashboardAuthenticationOptions _options;
    private readonly TimeProvider _timeProvider;

    private readonly DashboardUser _dummyUser;
    private readonly string _dummyPasswordHash;

    public DashboardPasswordAuthenticationService(
        IDashboardUserStore userStore,
        IPasswordHasher<DashboardUser> passwordHasher,
        IOptions<DashboardAuthenticationOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(userStore);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _userStore = userStore;
        _passwordHasher = passwordHasher;
        _options = options.Value;
        _timeProvider = timeProvider;

        DateTimeOffset createdAtUtc =
            DateTimeOffset.UnixEpoch;

        _dummyUser =
            new DashboardUser(
                Id: 0,
                Username: "dummy",
                NormalizedUsername: "DUMMY",
                DisplayName: "Dummy",
                PasswordHash: string.Empty,
                IsEnabled: false,
                FailedLoginCount: 0,
                LockoutEndUtc: null,
                SecurityStamp: "dummy",
                CreatedAtUtc: createdAtUtc,
                UpdatedAtUtc: createdAtUtc);

        _dummyPasswordHash =
            _passwordHasher.HashPassword(
                _dummyUser,
                "MissionControl-Dummy-Password");
    }

    public async Task<DashboardPasswordAuthenticationResult>
        AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (string.IsNullOrWhiteSpace(username))
        {
            ConsumePasswordVerificationWork(password);

            return DashboardPasswordAuthenticationResult
                .Failed();
        }

        string normalizedUsername =
            DashboardUsernameNormalizer.Normalize(
                username);

        DashboardUser? user =
            await _userStore
                .FindByNormalizedUsernameAsync(
                    normalizedUsername,
                    cancellationToken);

        if (user is null)
        {
            ConsumePasswordVerificationWork(password);

            return DashboardPasswordAuthenticationResult
                .Failed();
        }

        PasswordVerificationResult verificationResult =
            _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                password);

        DateTimeOffset nowUtc =
            _timeProvider.GetUtcNow();

        bool isLocked =
            user.LockoutEndUtc is DateTimeOffset lockoutEndUtc &&
            lockoutEndUtc > nowUtc;

        if (!user.IsEnabled || isLocked)
        {
            return DashboardPasswordAuthenticationResult
                .Failed();
        }

        if (verificationResult ==
            PasswordVerificationResult.Failed)
        {
            await _userStore.RecordFailedLoginAsync(
                user.Id,
                _options.MaxFailedAttempts,
                nowUtc,
                nowUtc.AddMinutes(
                    _options.LockoutMinutes),
                cancellationToken);

            return DashboardPasswordAuthenticationResult
                .Failed();
        }

        string? replacementPasswordHash =
            verificationResult ==
            PasswordVerificationResult.SuccessRehashNeeded
                ? _passwordHasher.HashPassword(
                    user,
                    password)
                : null;

        await _userStore.RecordSuccessfulLoginAsync(
            user.Id,
            replacementPasswordHash,
            nowUtc,
            cancellationToken);

        DashboardUser authenticatedUser =
            user with
            {
                PasswordHash =
                    replacementPasswordHash ??
                    user.PasswordHash,

                FailedLoginCount = 0,
                LockoutEndUtc = null,
                UpdatedAtUtc = nowUtc
            };

        return DashboardPasswordAuthenticationResult
            .Success(authenticatedUser);
    }

    private void ConsumePasswordVerificationWork(
        string providedPassword)
    {
        _passwordHasher.VerifyHashedPassword(
            _dummyUser,
            _dummyPasswordHash,
            providedPassword);
    }
}