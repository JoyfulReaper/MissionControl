extern alias AgentApp;

using AgentApp::MissionControl.Agent.Docker;
using AgentApp::MissionControl.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MissionControl.Tests;

public sealed class DockerMetricsCollectorTests
{
    [Fact]
    public async Task InventoryIncludesRunningExitedAndStoppedContainers()
    {
        int statsRequestCount = 0;
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
                    Json(++statsRequestCount == 1
                        ? CreateStatsJson(100, 1_000)
                        : CreateStatsJson(300, 2_000)),
                _ => throw new Xunit.Sdk.XunitException(
                    $"Unexpected Docker request: {requestUri}")
            });
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        DockerMetricsCollectionResult first =
            await collector.GetMetricsAsync(CancellationToken.None);
        DockerMetricsCollectionResult result =
            await collector.GetMetricsAsync(CancellationToken.None);

        Assert.Null(first.Containers[0].CpuPercent);
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
        int healthyStatsRequestCount = 0;
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
                    Json(++healthyStatsRequestCount == 1
                        ? CreateStatsJson(100, 1_000)
                        : CreateStatsJson(300, 2_000)),
                _ => throw new Xunit.Sdk.XunitException(
                    $"Unexpected Docker request: {requestUri}")
            });
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        await collector.GetMetricsAsync(CancellationToken.None);
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
    public async Task OneShotStatsUseCrossCycleCpuBaseline()
    {
        FakeDockerHandler handler = CreateSingleContainerHandler(
            statsRequest => Json(statsRequest == 1
                ? CreateStatsJson(100, 1_000)
                : CreateStatsJson(300, 2_000)));
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        ContainerMetric first = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);
        ContainerMetric second = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);

        Assert.Equal(900, first.MemoryUsageBytes);
        Assert.Equal(45, first.MemoryPercent);
        Assert.Null(first.CpuPercent);
        Assert.Equal(900, second.MemoryUsageBytes);
        Assert.Equal(40, second.CpuPercent);
        Assert.Equal(
            2,
            handler.RequestUris.Count(requestUri =>
                requestUri.EndsWith(
                    "/stats?stream=false&one-shot=true",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ValidIdleCpuSampleReturnsZero()
    {
        FakeDockerHandler handler = CreateSingleContainerHandler(
            statsRequest => Json(statsRequest == 1
                ? CreateStatsJson(100, 1_000)
                : CreateStatsJson(100, 2_000)));
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        await collector.GetMetricsAsync(CancellationToken.None);
        ContainerMetric idle = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);

        Assert.Equal(0, idle.CpuPercent);
    }

    [Fact]
    public async Task LargeCpuCountersDoNotOverflow()
    {
        FakeDockerHandler handler = CreateSingleContainerHandler(
            statsRequest => Json(statsRequest == 1
                ? CreateStatsJson(
                    ulong.MaxValue - 1_000,
                    ulong.MaxValue - 10_000,
                    onlineCpuCount: 8)
                : CreateStatsJson(
                    ulong.MaxValue - 500,
                    ulong.MaxValue - 5_000,
                    onlineCpuCount: 8)));
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        await collector.GetMetricsAsync(CancellationToken.None);
        ContainerMetric measured = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);

        Assert.Equal(80, measured.CpuPercent);
    }

    [Theory]
    [InlineData(6, 0, 60)]
    [InlineData(null, 4, 40)]
    public async Task CpuCountUsesOnlineCpusThenPerCpuFallback(
        int? onlineCpuCount,
        int perCpuCount,
        double expectedPercent)
    {
        FakeDockerHandler handler = CreateSingleContainerHandler(
            statsRequest => Json(CreateStatsJson(
                containerUsage:
                    statsRequest == 1 ? 100UL : 200UL,
                systemUsage:
                    statsRequest == 1 ? 1_000UL : 2_000UL,
                onlineCpuCount:
                    onlineCpuCount is null
                        ? null
                        : (ulong)onlineCpuCount.Value,
                perCpuCount: perCpuCount)));
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        await collector.GetMetricsAsync(CancellationToken.None);
        ContainerMetric measured = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);

        Assert.Equal(
            expectedPercent,
            Assert.IsType<double>(measured.CpuPercent),
            precision: 10);
    }

    [Fact]
    public async Task MalformedCpuDataDoesNotAffectOtherContainersOrMemory()
    {
        int brokenStatsCount = 0;
        int healthyStatsCount = 0;
        var handler = new FakeDockerHandler(
            requestUri => requestUri switch
            {
                "/version" => Json("""{ "ApiVersion": "1.45" }"""),
                "/v1.45/containers/json?all=true" => Json("""
                    [
                      {
                        "Id": "broken-id",
                        "Names": ["/broken"],
                        "Image": "example/broken:1",
                        "State": "running",
                        "RestartCount": 0
                      },
                      {
                        "Id": "healthy-id",
                        "Names": ["/healthy"],
                        "Image": "example/healthy:1",
                        "State": "running",
                        "RestartCount": 0
                      }
                    ]
                    """),
                "/v1.45/containers/broken-id/stats?stream=false&one-shot=true" =>
                    Json(++brokenStatsCount switch
                    {
                        1 => CreateStatsJson(100, 1_000),
                        2 => CreateStatsJson(
                            300,
                            2_000,
                            includeSystemUsage: false),
                        _ => CreateStatsJson(500, 3_000)
                    }),
                "/v1.45/containers/healthy-id/stats?stream=false&one-shot=true" =>
                    Json(++healthyStatsCount == 1
                        ? CreateStatsJson(100, 1_000)
                        : CreateStatsJson(300, 2_000)),
                _ => throw new Xunit.Sdk.XunitException(
                    $"Unexpected Docker request: {requestUri}")
            });
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        await collector.GetMetricsAsync(CancellationToken.None);
        DockerMetricsCollectionResult second =
            await collector.GetMetricsAsync(CancellationToken.None);

        ContainerMetric broken = second.Containers[0];
        ContainerMetric healthy = second.Containers[1];
        Assert.Equal(900, broken.MemoryUsageBytes);
        Assert.Equal(45, broken.MemoryPercent);
        Assert.Null(broken.CpuPercent);
        Assert.Equal(40, healthy.CpuPercent);

        ContainerMetric recovered = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers,
            container => container.Name == "broken");
        Assert.Equal(40, recovered.CpuPercent);
    }

    [Fact]
    public async Task FailedStatsRequestDoesNotCorruptCpuBaseline()
    {
        FakeDockerHandler handler = CreateSingleContainerHandler(
            statsRequest => statsRequest switch
            {
                1 => Json(CreateStatsJson(100, 1_000)),
                2 => Error(HttpStatusCode.InternalServerError),
                _ => Json(CreateStatsJson(500, 3_000))
            });
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        await collector.GetMetricsAsync(CancellationToken.None);
        ContainerMetric failed = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);
        ContainerMetric recovered = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);

        AssertResourceMetricsUnavailable(failed);
        Assert.Equal(40, recovered.CpuPercent);
    }

    [Fact]
    public async Task ReplacementWithSameNameStartsNewCpuBaseline()
    {
        FakeDockerHandler handler = CreateSingleContainerHandler(
            statsRequest => Json(CreateStatsJson(
                (ulong)statsRequest * 100,
                (ulong)statsRequest * 1_000)),
            inventoryRequest => inventoryRequest == 1
                ? "old-id"
                : "new-id");
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        ContainerMetric original = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);
        ContainerMetric replacement = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);
        ContainerMetric measuredReplacement = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);

        Assert.Null(original.CpuPercent);
        Assert.Null(replacement.CpuPercent);
        Assert.Equal(20, measuredReplacement.CpuPercent);
    }

    [Fact]
    public async Task DeletedContainerCpuBaselineIsPruned()
    {
        FakeDockerHandler handler = CreateSingleContainerHandler(
            statsRequest => Json(CreateStatsJson(
                (ulong)statsRequest * 100,
                (ulong)statsRequest * 1_000)),
            inventoryRequest => inventoryRequest == 2
                ? null
                : "running-id");
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        await collector.GetMetricsAsync(CancellationToken.None);
        Assert.Empty(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);
        ContainerMetric reappeared = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);
        ContainerMetric measured = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);

        Assert.Null(reappeared.CpuPercent);
        Assert.Equal(20, measured.CpuPercent);
    }

    [Fact]
    public async Task CpuCounterResetStartsNewBaseline()
    {
        FakeDockerHandler handler = CreateSingleContainerHandler(
            statsRequest => Json(statsRequest switch
            {
                1 => CreateStatsJson(1_000, 5_000),
                2 => CreateStatsJson(100, 1_000),
                _ => CreateStatsJson(300, 2_000)
            }));
        using DockerMetricsCollector collector =
            CreateCollector(handler);

        await collector.GetMetricsAsync(CancellationToken.None);
        ContainerMetric reset = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);
        ContainerMetric recovered = Assert.Single(
            (await collector.GetMetricsAsync(
                CancellationToken.None)).Containers);

        Assert.Null(reset.CpuPercent);
        Assert.Equal(40, recovered.CpuPercent);
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

    private static FakeDockerHandler CreateSingleContainerHandler(
        Func<int, HttpResponseMessage> statsResponseFactory,
        Func<int, string?>? containerIdFactory = null)
    {
        int inventoryRequestCount = 0;
        int statsRequestCount = 0;

        return new FakeDockerHandler(
            requestUri =>
            {
                if (requestUri == "/version")
                {
                    return Json("""{ "ApiVersion": "1.45" }""");
                }

                if (requestUri ==
                    "/v1.45/containers/json?all=true")
                {
                    string? containerId = containerIdFactory is null
                        ? "running-id"
                        : containerIdFactory(
                            ++inventoryRequestCount);

                    return containerId is null
                        ? Json("[]")
                        : Json($$"""
                            [
                              {
                                "Id": "{{containerId}}",
                                "Names": ["/api"],
                                "Image": "example/api:1",
                                "State": "running",
                                "RestartCount": 0
                              }
                            ]
                            """);
                }

                if (requestUri.EndsWith(
                        "/stats?stream=false&one-shot=true",
                        StringComparison.Ordinal))
                {
                    return statsResponseFactory(
                        ++statsRequestCount);
                }

                throw new Xunit.Sdk.XunitException(
                    $"Unexpected Docker request: {requestUri}");
            });
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

    private static string CreateStatsJson(
        ulong containerUsage,
        ulong systemUsage,
        ulong? onlineCpuCount = 2,
        int perCpuCount = 0,
        bool includeSystemUsage = true)
    {
        var cpuUsage = new Dictionary<string, object>
        {
            ["total_usage"] = containerUsage
        };

        if (perCpuCount > 0)
        {
            cpuUsage["percpu_usage"] =
                Enumerable.Repeat(0UL, perCpuCount).ToArray();
        }

        var cpuStats = new Dictionary<string, object>
        {
            ["cpu_usage"] = cpuUsage
        };

        if (includeSystemUsage)
        {
            cpuStats["system_cpu_usage"] = systemUsage;
        }

        if (onlineCpuCount is not null)
        {
            cpuStats["online_cpus"] = onlineCpuCount.Value;
        }

        return JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["memory_stats"] = new
                {
                    usage = 1_000,
                    limit = 2_000,
                    stats = new
                    {
                        inactive_file = 100
                    }
                },
                ["cpu_stats"] = cpuStats,
                ["precpu_stats"] =
                    new Dictionary<string, object>()
            });
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
