extern alias AgentApp;

using AgentApp::MissionControl.Agent;
using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Protocols;
using Xunit;

namespace MissionControl.Tests;

public sealed class ProtocolProbeRunnerTests
{
    [Fact]
    public async Task FailedProbeRetainsObservedEndpointAndInternalDiagnostic()
    {
        var runner = new ProtocolProbeRunner(
            [
                new ThrowingProbe()
            ]);

        ProtocolProbeResult result = Assert.Single(
            await runner.RunAsync(
                [CreateOptions()],
                CancellationToken.None));

        Assert.False(result.Succeeded);
        Assert.Equal("example.internal:17", result.Endpoint);
        Assert.Contains("InvalidOperationException", result.Error);
        Assert.Contains("Connection refused", result.Error);
        Assert.IsType<long>(result.DurationMilliseconds);
    }

    [Fact]
    public async Task SuccessfulProbeHasEndpointLongDurationAndNullError()
    {
        var runner = new ProtocolProbeRunner(
            [
                new SuccessfulProbe()
            ]);

        ProtocolProbeResult result = Assert.Single(
            await runner.RunAsync(
                [CreateOptions()],
                CancellationToken.None));

        Assert.True(result.Succeeded);
        Assert.Equal("example.internal:17", result.Endpoint);
        Assert.Null(result.Error);
        Assert.IsType<long>(result.DurationMilliseconds);
    }

    private static ProbeOptions CreateOptions()
    {
        return new ProbeOptions
        {
            Name = "qotd",
            Host = "example.internal",
            Protocol = "test",
            Port = 17,
            TimeoutMilliseconds = 1_000
        };
    }

    private sealed class ThrowingProbe : IProtocolProbe
    {
        public string Protocol => "test";

        public Task ExecuteAsync(
            ProbeOptions options,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "Connection refused\ninternal detail");
        }
    }

    private sealed class SuccessfulProbe : IProtocolProbe
    {
        public string Protocol => "test";

        public Task ExecuteAsync(
            ProbeOptions options,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
