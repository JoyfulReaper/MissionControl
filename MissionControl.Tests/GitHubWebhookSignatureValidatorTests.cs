using Microsoft.Extensions.Options;
using MissionControl.Gateway.Integrations.GitHub;
using System.Text;
using Xunit;

namespace MissionControl.Tests;

public sealed class GitHubWebhookSignatureValidatorTests
{
    [Fact]
    public void ValidSha256SignatureIsAccepted()
    {
        var validator = CreateValidator("key");
        byte[] payload =
            Encoding.UTF8.GetBytes(
                "The quick brown fox jumps over the lazy dog");

        const string fixedVector =
            "sha256=f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8";

        Assert.True(validator.IsValid(payload, fixedVector));
    }

    [Fact]
    public void AlteredPayloadUsingOldSignatureIsRejected()
    {
        var body = Encoding.UTF8.GetBytes("""{"ok":true}""");
        var validator = CreateValidator();
        string signature = GitHubTestPayloads.Sign(body);

        Assert.False(
            validator.IsValid(
                Encoding.UTF8.GetBytes("""{"ok":false}"""),
                signature));
    }

    [Fact]
    public void DifferentSecretIsRejected()
    {
        byte[] body = Encoding.UTF8.GetBytes("""{"ok":true}""");

        Assert.False(
            CreateValidator("different-secret-32-characters")
                .IsValid(body, GitHubTestPayloads.Sign(body)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingOrEmptySignatureIsRejected(
        string? signature)
    {
        Assert.False(
            CreateValidator().IsValid(
                Encoding.UTF8.GetBytes("{}"),
                signature));
    }

    [Fact]
    public void SignatureWithoutSha256PrefixIsRejected()
    {
        byte[] body = Encoding.UTF8.GetBytes("{}");
        string signature = GitHubTestPayloads.Sign(body)["sha256=".Length..];

        Assert.False(CreateValidator().IsValid(body, signature));
    }

    [Fact]
    public void NonHexadecimalSignatureTextIsRejected()
    {
        Assert.False(
            CreateValidator().IsValid(
                Encoding.UTF8.GetBytes("{}"),
                "sha256=not-hexadecimal"));
    }

    [Fact]
    public void ValidHexadecimalSignatureWithWrongByteLengthIsRejected()
    {
        Assert.False(
            CreateValidator().IsValid(
                Encoding.UTF8.GetBytes("{}"),
                "sha256=abcdef"));
    }

    [Fact]
    public void UppercaseHexadecimalIsAccepted()
    {
        byte[] body = Encoding.UTF8.GetBytes("{}");

        Assert.True(
            CreateValidator().IsValid(
                body,
                GitHubTestPayloads.Sign(body).ToUpperInvariant()));
    }

    [Fact]
    public void SignatureUsesCompletePayloadBytesIncludingWhitespaceAndLineEndings()
    {
        byte[] body = Encoding.UTF8.GetBytes("{\r\n  \"ok\": true\r\n}\n");
        string signature = GitHubTestPayloads.Sign(body);

        Assert.True(CreateValidator().IsValid(body, signature));
        Assert.False(
            CreateValidator().IsValid(
                Encoding.UTF8.GetBytes("{\n  \"ok\": true\n}\n"),
                signature));
    }

    private static GitHubWebhookSignatureValidator CreateValidator(
        string secret = GatewayTestApplicationFactory.WebhookSecret)
    {
        return new GitHubWebhookSignatureValidator(
            Options.Create(
                new GitHubWebhookOptions
                {
                    Enabled = true,
                    Secret = secret,
                    AllowedOwner = "JoyfulReaper"
                }));
    }
}
