using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace MissionControl.Gateway.Integrations.GitHub;

public sealed class GitHubWebhookSignatureValidator(
    IOptions<GitHubWebhookOptions> options,
    ILogger<GitHubWebhookSignatureValidator> logger)
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
            logger.LogWarning(
                "Webhook signature header was missing or malformed.");

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
            logger.LogWarning(
                "Webhook signature was not valid hexadecimal.");

            return false;
        }

        if (suppliedSignature.Length != Sha256Length)
        {
            logger.LogWarning(
                "Webhook signature length was {Length}, expected 32.",
                suppliedSignature.Length);

            return false;
        }

        byte[] expectedSignature =
            HMACSHA256.HashData(_secret, payload);

        string secretFingerprint =
            Convert.ToHexString(
                SHA256.HashData(_secret))[..12];

        string payloadHash =
            Convert.ToHexString(
                SHA256.HashData(payload));

        logger.LogWarning(
            """
            GitHub webhook signature diagnostic:
            SecretLength={SecretLength}
            SecretFingerprint={SecretFingerprint}
            PayloadLength={PayloadLength}
            PayloadSha256={PayloadSha256}
            SuppliedSignature={SuppliedSignature}
            ExpectedSignature={ExpectedSignature}
            """,
            _secret.Length,
            secretFingerprint,
            payload.Length,
            payloadHash,
            Convert.ToHexString(suppliedSignature),
            Convert.ToHexString(expectedSignature));

        return CryptographicOperations.FixedTimeEquals(
            expectedSignature,
            suppliedSignature);
    }
}