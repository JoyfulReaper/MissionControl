extern alias AgentApp;

using AgentApp::MissionControl.Agent.Endpoints;
using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Storage;
using Xunit;

namespace MissionControl.Tests;

public sealed class PublicDiagnosticSanitizerTests
{
    [Fact]
    public void SuccessfulProbeNeverExposesStaleFailureText()
    {
        Assert.Null(
            PublicDiagnosticSanitizer.SanitizeError(
                "Connection refused",
                succeeded: true));
    }

    [Fact]
    public void MultilineStackTraceAndControlCharactersAreNotExposed()
    {
        string diagnostic =
            "System.InvalidOperationException: Connection\trefused\0now" +
            Environment.NewLine +
            "   at Example.Probe.Run() in C:\\source\\Probe.cs:line 42";

        string sanitized = Assert.IsType<string>(
            PublicDiagnosticSanitizer.SanitizeError(
                diagnostic,
                succeeded: false));

        Assert.Equal("Connection refused now", sanitized);
        Assert.DoesNotContain("InvalidOperationException", sanitized);
        Assert.DoesNotContain("Example.Probe", sanitized);
        Assert.DoesNotContain('\n', sanitized);
        Assert.DoesNotContain('\r', sanitized);
        Assert.DoesNotContain('\0', sanitized);
    }

    [Fact]
    public void PublicErrorIsLengthLimited()
    {
        string sanitized = Assert.IsType<string>(
            PublicDiagnosticSanitizer.SanitizeError(
                new string('x', 2_000),
                succeeded: false));

        Assert.Equal(
            PublicDiagnosticSanitizer.MaximumErrorLength,
            sanitized.Length);
        Assert.EndsWith("…", sanitized);
    }

    [Theory]
    [InlineData(
        "TimeoutException: Timed out while connecting.",
        "Timed out")]
    [InlineData(
        "SocketException (111): Connection refused",
        "Connection refused")]
    public void UsefulFailureCategoryRemainsRecognizable(
        string diagnostic,
        string expectedText)
    {
        string sanitized = Assert.IsType<string>(
            PublicDiagnosticSanitizer.SanitizeError(
                diagnostic,
                succeeded: false));

        Assert.Contains(
            expectedText,
            sanitized,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FocusedSensitiveValuesAndDockerPathsAreRedacted()
    {
        const string diagnostic =
            "Failed at https://user:example-pass@example.test " +
            "token=example-token Bearer example-bearer " +
            "via /var/run/docker.sock";

        string sanitized = Assert.IsType<string>(
            PublicDiagnosticSanitizer.SanitizeError(
                diagnostic,
                succeeded: false));

        Assert.DoesNotContain("example-pass", sanitized);
        Assert.DoesNotContain("example-token", sanitized);
        Assert.DoesNotContain("example-bearer", sanitized);
        Assert.DoesNotContain("/var/run/docker.sock", sanitized);
        Assert.Contains("[redacted]", sanitized);
    }

    [Fact]
    public void EndpointRemainsStableButDoesNotExposeCredentials()
    {
        Assert.Equal(
            "example.internal:17",
            PublicDiagnosticSanitizer.SanitizeEndpoint(
                "example.internal:17"));

        string sanitized = Assert.IsType<string>(
            PublicDiagnosticSanitizer.SanitizeEndpoint(
                "tcp://user:example-pass@example.internal:17\r\nignored"));

        Assert.Equal(
            "tcp://[redacted]@example.internal:17",
            sanitized);
        Assert.DoesNotContain("example-pass", sanitized);
        Assert.DoesNotContain("ignored", sanitized);
    }

    [Fact]
    public void ProjectionSanitizesWithoutMutatingStoredDiagnostic()
    {
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        const string rawDiagnostic =
            "SocketException: Connection refused\n   at Internal.Probe";
        var stored = new StoredNodeSnapshot(
            Snapshot: new NodeSnapshotEvent(
                Node: "node-1",
                CapturedAt: capturedAt,
                Host: null,
                Protocols:
                [
                    new ProtocolProbeResult(
                        "echo",
                        "localhost:7",
                        false,
                        15,
                        rawDiagnostic)
                ],
                Containers: [],
                DockerAvailable: true,
                DockerError: null),
            PublishSucceeded: null,
            LastPublishAttemptAt: null,
            UpdatedAt: capturedAt);

        var projected =
            AgentSnapshotEndpointRouteBuilderExtensions
                .CreatePublicSnapshot(
                    stored,
                    capturedAt,
                    TimeSpan.FromMinutes(1));

        Assert.Equal(
            rawDiagnostic,
            Assert.Single(stored.Snapshot.Protocols).Error);
        Assert.Equal(
            "Connection refused",
            Assert.Single(projected.Protocols).Error);
    }
}
