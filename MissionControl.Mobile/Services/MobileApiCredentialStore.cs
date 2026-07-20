namespace MissionControl.Mobile.Services;

public sealed class MobileApiCredentialStore
{
    private const string TokenKey =
        "mission-control-mobile-api-token";

    public Task<string?> GetTokenAsync()
    {
        return SecureStorage.Default.GetAsync(
            TokenKey);
    }

    public Task SetTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException(
                "The Mobile API token is required.",
                nameof(token));
        }

        return SecureStorage.Default.SetAsync(
            TokenKey,
            token.Trim());
    }

    public bool RemoveToken()
    {
        return SecureStorage.Default.Remove(
            TokenKey);
    }
}