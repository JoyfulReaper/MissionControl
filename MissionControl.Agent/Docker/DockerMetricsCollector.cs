using Microsoft.Extensions.Options;
using MissionControl.Agent.Models;
using System.Net.Sockets;
using System.Text.Json;

namespace MissionControl.Agent.Docker;

internal sealed class DockerMetricsCollector :
    IDockerMetricsCollector,
    IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DockerMetricsCollector> _logger;

    private string? _apiPrefix;

    public DockerMetricsCollector(
        IOptions<AgentOptions> options,
        ILogger<DockerMetricsCollector> logger) :
        this(CreateHttpClient(options.Value), logger)
    {
    }

    internal DockerMetricsCollector(
        HttpClient httpClient,
        ILogger<DockerMetricsCollector> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DockerMetricsCollectionResult>
        GetMetricsAsync(
            CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<ContainerMetric> containers =
                await CollectMetricsAsync(cancellationToken);

            return new DockerMetricsCollectionResult(
                Succeeded: true,
                Containers: containers,
                Error: null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(
                exception,
                "Docker metric collection timed out.");

            return new DockerMetricsCollectionResult(
                Succeeded: false,
                Containers: [],
                Error: "Docker metric collection timed out.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Docker metric collection is unavailable.");

            return new DockerMetricsCollectionResult(
                Succeeded: false,
                Containers: [],
                Error: "Docker metric collection is unavailable.");
        }
    }

    private async Task<IReadOnlyList<ContainerMetric>>
        CollectMetricsAsync(
            CancellationToken cancellationToken)
    {
        string apiPrefix = await GetApiPrefixAsync(
            cancellationToken);

        using JsonDocument document = await GetJsonAsync(
            $"{apiPrefix}/containers/json?all=true",
            cancellationToken);

        var metrics = new List<ContainerMetric>();

        foreach (JsonElement container in
                 document.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string id = GetString(container, "Id");

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string name = GetContainerName(container);
            string image = GetString(container, "Image");
            string state = NormalizeState(
                GetString(container, "State"));
            int? restartCount =
                GetRestartCount(container);

            if (restartCount is null)
            {
                restartCount = await TryGetRestartCountAsync(
                    apiPrefix,
                    id,
                    name,
                    cancellationToken);
            }

            ResourceMetrics resourceMetrics =
                string.Equals(
                    state,
                    "running",
                    StringComparison.Ordinal)
                    ? await TryGetResourceMetricsAsync(
                        apiPrefix,
                        id,
                        name,
                        cancellationToken)
                    : ResourceMetrics.Unavailable;

            metrics.Add(
                new ContainerMetric(
                    Name: name,
                    Image: image,
                    State: state,
                    MemoryUsageBytes:
                        resourceMetrics.MemoryUsageBytes,
                    MemoryLimitBytes:
                        resourceMetrics.MemoryLimitBytes,
                    MemoryPercent:
                        resourceMetrics.MemoryPercent,
                    CpuPercent:
                        resourceMetrics.CpuPercent,
                    RestartCount: restartCount));
        }

        return metrics;
    }

    private async Task<int?> TryGetRestartCountAsync(
        string apiPrefix,
        string id,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument inspectDocument = await GetJsonAsync(
                $"{apiPrefix}/containers/{id}/json",
                cancellationToken);

            return GetRestartCount(
                inspectDocument.RootElement);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Docker restart count is unavailable for container {Container}.",
                name);

            return null;
        }
    }

    private async Task<ResourceMetrics>
        TryGetResourceMetricsAsync(
            string apiPrefix,
            string id,
            string name,
            CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument statsDocument = await GetJsonAsync(
                $"{apiPrefix}/containers/{id}/stats" +
                "?stream=false&one-shot=true",
                cancellationToken);

            JsonElement stats = statsDocument.RootElement;
            long? memoryUsageBytes =
                CalculateMemoryUsage(stats);
            long? memoryLimitBytes =
                GetOptionalNestedInt64(
                    stats,
                    "memory_stats",
                    "limit");
            double? memoryPercent =
                memoryUsageBytes is not null &&
                memoryLimitBytes is > 0
                    ? memoryUsageBytes.Value /
                      (double)memoryLimitBytes.Value * 100.0
                    : null;

            return new ResourceMetrics(
                MemoryUsageBytes: memoryUsageBytes,
                MemoryLimitBytes: memoryLimitBytes,
                MemoryPercent: memoryPercent,
                CpuPercent: CalculateCpuPercent(stats));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Docker resource metrics are unavailable for container {Container}.",
                name);

            return ResourceMetrics.Unavailable;
        }
    }

    private async Task<string> GetApiPrefixAsync(
        CancellationToken cancellationToken)
    {
        if (_apiPrefix is not null)
        {
            return _apiPrefix;
        }

        using JsonDocument document = await GetJsonAsync(
            "/version",
            cancellationToken);

        string apiVersion = GetString(
            document.RootElement,
            "ApiVersion");

        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            throw new InvalidDataException(
                "Docker did not return an API version.");
        }

        _apiPrefix = $"/v{apiVersion}";

        return _apiPrefix;
    }

    private async Task<JsonDocument> GetJsonAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    private static HttpClient CreateHttpClient(
        AgentOptions agentOptions)
    {
        string socketPath = agentOptions.DockerSocketPath;
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(
                    AddressFamily.Unix,
                    SocketType.Stream,
                    ProtocolType.Unspecified);

                try
                {
                    var endpoint =
                        new UnixDomainSocketEndPoint(socketPath);

                    await socket.ConnectAsync(
                        endpoint,
                        cancellationToken);

                    return new NetworkStream(
                        socket,
                        ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };

        return new HttpClient(
            handler,
            disposeHandler: true)
        {
            BaseAddress = new Uri("http://docker"),
            Timeout = TimeSpan.FromSeconds(
                agentOptions.DockerTimeoutSeconds)
        };
    }

    private static long? CalculateMemoryUsage(
        JsonElement stats)
    {
        if (!stats.TryGetProperty(
                "memory_stats",
                out JsonElement memoryStats))
        {
            return null;
        }

        long? totalUsage =
            GetOptionalInt64(memoryStats, "usage");

        if (totalUsage is null)
        {
            return null;
        }

        long cache = 0;

        if (memoryStats.TryGetProperty(
                "stats",
                out JsonElement memoryDetails))
        {
            cache =
                GetOptionalInt64(
                    memoryDetails,
                    "inactive_file") ??
                GetOptionalInt64(
                    memoryDetails,
                    "total_inactive_file") ??
                0;
        }

        return Math.Max(0, totalUsage.Value - cache);
    }

    private static double? CalculateCpuPercent(
        JsonElement stats)
    {
        if (!stats.TryGetProperty(
                "cpu_stats",
                out JsonElement currentCpu) ||
            !stats.TryGetProperty(
                "precpu_stats",
                out JsonElement previousCpu))
        {
            return null;
        }

        long? currentContainerUsage =
            GetOptionalNestedInt64(
                currentCpu,
                "cpu_usage",
                "total_usage");
        long? previousContainerUsage =
            GetOptionalNestedInt64(
                previousCpu,
                "cpu_usage",
                "total_usage");
        long? currentSystemUsage =
            GetOptionalInt64(
                currentCpu,
                "system_cpu_usage");
        long? previousSystemUsage =
            GetOptionalInt64(
                previousCpu,
                "system_cpu_usage");

        if (currentContainerUsage is null ||
            previousContainerUsage is null ||
            currentSystemUsage is null ||
            previousSystemUsage is null)
        {
            return null;
        }

        long containerDelta =
            currentContainerUsage.Value -
            previousContainerUsage.Value;
        long systemDelta =
            currentSystemUsage.Value -
            previousSystemUsage.Value;

        if (containerDelta <= 0 || systemDelta <= 0)
        {
            return 0;
        }

        long onlineCpuCount =
            GetOptionalInt64(currentCpu, "online_cpus") ??
            0;

        if (onlineCpuCount <= 0 &&
            currentCpu.TryGetProperty(
                "cpu_usage",
                out JsonElement cpuUsage) &&
            cpuUsage.TryGetProperty(
                "percpu_usage",
                out JsonElement perCpuUsage) &&
            perCpuUsage.ValueKind == JsonValueKind.Array)
        {
            onlineCpuCount =
                perCpuUsage.GetArrayLength();
        }

        onlineCpuCount = Math.Max(1, onlineCpuCount);

        return containerDelta /
               (double)systemDelta *
               onlineCpuCount *
               100.0;
    }

    private static string GetContainerName(
        JsonElement container)
    {
        if (container.TryGetProperty(
                "Names",
                out JsonElement names) &&
            names.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement nameElement in
                     names.EnumerateArray())
            {
                string? name = nameElement.GetString();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim().TrimStart('/');
                }
            }
        }

        string id = GetString(container, "Id");

        return id.Length > 12
            ? id[..12]
            : id;
    }

    private static string NormalizeState(string state)
    {
        return string.IsNullOrWhiteSpace(state)
            ? "unknown"
            : state.Trim().ToLowerInvariant();
    }

    private static int? GetRestartCount(
        JsonElement element)
    {
        long? value =
            GetOptionalInt64(element, "RestartCount");

        return value switch
        {
            null => null,
            < 0 => 0,
            > int.MaxValue => int.MaxValue,
            _ => (int)value.Value
        };
    }

    private static string GetString(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(
                   propertyName,
                   out JsonElement property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static long? GetOptionalInt64(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out JsonElement property))
        {
            return null;
        }

        return property.TryGetInt64(out long value)
            ? value
            : null;
    }

    private static long? GetOptionalNestedInt64(
        JsonElement element,
        string parentProperty,
        string childProperty)
    {
        if (!element.TryGetProperty(
                parentProperty,
                out JsonElement parent))
        {
            return null;
        }

        return GetOptionalInt64(
            parent,
            childProperty);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record ResourceMetrics(
        long? MemoryUsageBytes,
        long? MemoryLimitBytes,
        double? MemoryPercent,
        double? CpuPercent)
    {
        public static ResourceMetrics Unavailable { get; } =
            new(null, null, null, null);
    }
}
