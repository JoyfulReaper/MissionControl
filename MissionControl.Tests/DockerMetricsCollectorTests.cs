extern alias AgentApp;

using AgentApp::MissionControl.Agent.Docker;
using AgentApp::MissionControl.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using Xunit;

namespace MissionControl.Tests;

public sealed class DockerMetricsCollectorTests
{
    [Fact]
    public async Task InventoryIncludesRunningExitedAndStoppedContainers()
    {
        var handler = new FakeDockerHandler(
            requestUri => requestUri switch
            {
                "/version" => Json("""
                    { "ApiVersion": "1.45" }
                    """),
                "/v1.45/containers/json?all=true" => Json("""
                    [
                      {
                        "Id": "running-id",
                        "Names": ["/api"],
                        "Image": "missioncontrol/api:1",
                        "State": "RUNNING"
                      },
                      {
                        "Id": "exited-id",
                        "Names": ["/worker"],
                        "Image": "missioncontrol/worker:1",
                        "State": "exited"
                      },
                      {
                        "Id": "stopped-id",
                        "Names": ["/scheduler"],
                        "Image": "missioncontrol/scheduler:1",
                        "State": "stopped"
                      }
                    ]
                    """),
                "/v1.45/containers/running-id/json" =>
                    Json("""{ "RestartCount": 2 }"""),
                "/v1.45/containers/exited-id/json" =>
                    Json("""{ "RestartCount": 5 }"""),
                "/v1.45/containers/stopped-id/json" =>
                    Json("""{ "RestartCount": 7 }"""),
                "/v1.45/containers/running-id/stats?stream=false&one-shot=true" =>
                    Json(CreateStatsJson()),
                _ => throw new Xunit.Sdk.XunitException(
                    $"Unexpected Docker request: {requestUri}")
            });
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        DockerMetricsCollectionResult result =
            await collector.GetMetricsAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.Equal(3, result.Containers.Count);
        Assert.Contains(
            "/v1.45/containers/json?all=true",
            handler.RequestUris);
        Assert.DoesNotContain(
            "/v1.45/containers/json?all=false",
            handler.RequestUris);

        ContainerMetric running = result.Containers[0];
        Assert.Equal("api", running.Name);
        Assert.Equal("missioncontrol/api:1", running.Image);
        Assert.Equal("running", running.State);
        Assert.Equal(900, running.MemoryUsageBytes);
        Assert.Equal(2_000, running.MemoryLimitBytes);
        Assert.Equal(45, running.MemoryPercent);
        Assert.Equal(40, running.CpuPercent);
        Assert.Equal(2, running.RestartCount);

        ContainerMetric exited = result.Containers[1];
        Assert.Equal("worker", exited.Name);
        Assert.Equal("exited", exited.State);
        Assert.Equal(5, exited.RestartCount);
        AssertResourceMetricsUnavailable(exited);

        ContainerMetric stopped = result.Containers[2];
        Assert.Equal("scheduler", stopped.Name);
        Assert.Equal("stopped", stopped.State);
        Assert.Equal(7, stopped.RestartCount);
        AssertResourceMetricsUnavailable(stopped);

        Assert.DoesNotContain(
            handler.RequestUris,
            requestUri =>
                requestUri.Contains(
                    "exited-id/stats",
                    StringComparison.Ordinal) ||
                requestUri.Contains(
                    "stopped-id/stats",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunningStatsFailureRetainsContainerAndDoesNotRemoveOthers()
    {
        var handler = new FakeDockerHandler(
            requestUri => requestUri switch
            {
                "/version" => Json("""
                    { "ApiVersion": "1.45" }
                    """),
                "/v1.45/containers/json?all=true" => Json("""
                    [
                      {
                        "Id": "unavailable-id",
                        "Names": ["/unavailable"],
                        "Image": "example/unavailable:1",
                        "State": "running"
                      },
                      {
                        "Id": "healthy-id",
                        "Names": ["/healthy"],
                        "Image": "example/healthy:1",
                        "State": "running"
                      }
                    ]
                    """),
                "/v1.45/containers/unavailable-id/json" =>
                    Json("""{ "RestartCount": 4 }"""),
                "/v1.45/containers/healthy-id/json" =>
                    Json("""{ "RestartCount": 1 }"""),
                "/v1.45/containers/unavailable-id/stats?stream=false&one-shot=true" =>
                    Error(HttpStatusCode.InternalServerError),
                "/v1.45/containers/healthy-id/stats?stream=false&one-shot=true" =>
                    Json(CreateStatsJson()),
                _ => throw new Xunit.Sdk.XunitException(
                    $"Unexpected Docker request: {requestUri}")
            });
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        DockerMetricsCollectionResult result =
            await collector.GetMetricsAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Containers.Count);

        ContainerMetric unavailable = result.Containers[0];
        Assert.Equal("unavailable", unavailable.Name);
        Assert.Equal("running", unavailable.State);
        Assert.Equal(4, unavailable.RestartCount);
        AssertResourceMetricsUnavailable(unavailable);

        ContainerMetric healthy = result.Containers[1];
        Assert.Equal("healthy", healthy.Name);
        Assert.Equal(900, healthy.MemoryUsageBytes);
        Assert.Equal(40, healthy.CpuPercent);
    }

    [Fact]
    public async Task EmptyInventoryAndDockerFailureRemainDistinct()
    {
        var emptyHandler = new FakeDockerHandler(
            requestUri => requestUri switch
            {
                "/version" => Json("""
                    { "ApiVersion": "1.45" }
                    """),
                "/v1.45/containers/json?all=true" => Json("[]"),
                _ => throw new Xunit.Sdk.XunitException(
                    $"Unexpected Docker request: {requestUri}")
            });
        using DockerMetricsCollector emptyCollector =
            CreateCollector(emptyHandler);

        DockerMetricsCollectionResult empty =
            await emptyCollector.GetMetricsAsync(
                CancellationToken.None);

        Assert.True(empty.Succeeded);
        Assert.Empty(empty.Containers);
        Assert.Null(empty.Error);

        var failedHandler = new FakeDockerHandler(
            requestUri => requestUri == "/version"
                ? Json("""{ "ApiVersion": "1.45" }""")
                : Error(HttpStatusCode.ServiceUnavailable));
        using DockerMetricsCollector failedCollector =
            CreateCollector(failedHandler);

        DockerMetricsCollectionResult failed =
            await failedCollector.GetMetricsAsync(
                CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.Empty(failed.Containers);
        Assert.Equal(
            "Docker metric collection is unavailable.",
            failed.Error);
    }

    [Fact]
    public async Task InspectFailureRetainsNonRunningContainer()
    {
        var handler = new FakeDockerHandler(
            requestUri => requestUri switch
            {
                "/version" => Json("""
                    { "ApiVersion": "1.45" }
                    """),
                "/v1.45/containers/json?all=true" => Json("""
                    [
                      {
                        "Id": "created-id",
                        "Names": ["/pending"],
                        "Image": "example/pending:1",
                        "State": "created"
                      }
                    ]
                    """),
                "/v1.45/containers/created-id/json" =>
                    Error(HttpStatusCode.InternalServerError),
                _ => throw new Xunit.Sdk.XunitException(
                    $"Unexpected Docker request: {requestUri}")
            });
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        DockerMetricsCollectionResult result =
            await collector.GetMetricsAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        ContainerMetric container =
            Assert.Single(result.Containers);
        Assert.Equal("pending", container.Name);
        Assert.Equal("created", container.State);
        Assert.Null(container.RestartCount);
        AssertResourceMetricsUnavailable(container);
        Assert.DoesNotContain(
            handler.RequestUris,
            requestUri => requestUri.Contains(
                "/stats",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplicationShutdownCancellationPropagates()
    {
        var handler = new FakeDockerHandler(
            _ => throw new Xunit.Sdk.XunitException(
                "A canceled request should not reach Docker."));
        using DockerMetricsCollector collector =
            CreateCollector(handler);
        using var cancellationSource =
            new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => collector.GetMetricsAsync(
                cancellationSource.Token));
    }

    private static DockerMetricsCollector CreateCollector(
        HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://docker")
        };

        return new DockerMetricsCollector(
            client,
            NullLogger<DockerMetricsCollector>.Instance);
    }

    private static void AssertResourceMetricsUnavailable(
        ContainerMetric container)
    {
        Assert.Null(container.MemoryUsageBytes);
        Assert.Null(container.MemoryLimitBytes);
        Assert.Null(container.MemoryPercent);
        Assert.Null(container.CpuPercent);
    }

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

    private static HttpResponseMessage Error(
        HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                "Docker request failed.",
                Encoding.UTF8,
                "text/plain")
        };
    }

    private static string CreateStatsJson()
    {
        return """
            {
              "memory_stats": {
                "usage": 1000,
                "limit": 2000,
                "stats": {
                  "inactive_file": 100
                }
              },
              "cpu_stats": {
                "cpu_usage": {
                  "total_usage": 300
                },
                "system_cpu_usage": 2000,
                "online_cpus": 2
              },
              "precpu_stats": {
                "cpu_usage": {
                  "total_usage": 100
                },
                "system_cpu_usage": 1000
              }
            }
            """;
    }

    private sealed class FakeDockerHandler(
        Func<string, HttpResponseMessage> responseFactory) :
        HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string requestUri =
                request.RequestUri?.PathAndQuery ??
                string.Empty;
            RequestUris.Add(requestUri);

            return Task.FromResult(
                responseFactory(requestUri));
        }
    }
}
