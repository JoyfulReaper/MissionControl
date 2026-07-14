using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace MissionControl.Gateway.Integrations.GitHub;

public sealed class GitHubWebhookSignatureValidator(
    IOptions<GitHubWebhookOptions> options)
{
    private const string Prefix = "sha256=";
    private const int Sha256Length = 32;

    private readonly byte[] _secret =
        Encoding.UTF8.GetBytes(options.Value.Secret);

    public bool IsValid(
        ReadOnlySpan<byte> payload,
        string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) ||
            !signatureHeader.StartsWith(
                Prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] suppliedSignature;

        try
        {
            suppliedSignature = Convert.FromHexString(
                signatureHeader[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (suppliedSignature.Length != Sha256Length)
        {
            return false;
        }

        byte[] expectedSignature =
            HMACSHA256.HashData(_secret, payload);

        return CryptographicOperations.FixedTimeEquals(
            expectedSignature,
            suppliedSignature);
    }
}
