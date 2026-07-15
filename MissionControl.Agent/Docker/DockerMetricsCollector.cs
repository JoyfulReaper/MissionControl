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
        ILogger<DockerMetricsCollector> logger)
    {
        _logger = logger;

        var agentOptions = options.Value;
        var socketPath = agentOptions.DockerSocketPath;

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

        _httpClient = new HttpClient(
            handler,
            disposeHandler: true)
        {
            BaseAddress = new Uri("http://docker"),
            Timeout = TimeSpan.FromSeconds(
                agentOptions.DockerTimeoutSeconds)
        };
    }

    public async Task<IReadOnlyList<ContainerMetric>>
        GetMetricsAsync(
            CancellationToken cancellationToken = default)
    {
        var apiPrefix = await GetApiPrefixAsync(
            cancellationToken);

        using var document = await GetJsonAsync(
            $"{apiPrefix}/containers/json?all=false",
            cancellationToken);

        var metrics = new List<ContainerMetric>();

        foreach (var container in
                 document.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = GetString(container, "Id");
            var name = GetContainerName(container);
            var image = GetString(container, "Image");
            var state = GetString(container, "State");

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            try
            {
                var metric = await GetContainerMetricAsync(
                    apiPrefix,
                    id,
                    name,
                    image,
                    state,
                    cancellationToken);

                metrics.Add(metric);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Timed out while collecting Docker metrics for container {Container}.",
                    name);
            }
            catch (HttpRequestException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Docker returned an error while collecting metrics for container {Container}.",
                    name);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Docker returned invalid metric data for container {Container}.",
                    name);
            }
        }

        return metrics;
    }

    private async Task<ContainerMetric>
        GetContainerMetricAsync(
            string apiPrefix,
            string id,
            string name,
            string image,
            string state,
            CancellationToken cancellationToken)
    {
        using var statsDocument = await GetJsonAsync(
            $"{apiPrefix}/containers/{id}/stats" +
            "?stream=false&one-shot=true",
            cancellationToken);

        using var inspectDocument = await GetJsonAsync(
            $"{apiPrefix}/containers/{id}/json",
            cancellationToken);

        var stats = statsDocument.RootElement;
        var inspect = inspectDocument.RootElement;

        var memoryUsageBytes =
            CalculateMemoryUsage(stats);

        var memoryLimitBytes =
            GetNestedInt64(
                stats,
                "memory_stats",
                "limit");

        var memoryPercent =
            memoryLimitBytes > 0
                ? memoryUsageBytes /
                  (double)memoryLimitBytes * 100.0
                : 0.0;

        var cpuPercent =
            CalculateCpuPercent(stats);

        var restartCountValue =
            GetInt64(inspect, "RestartCount");

        var restartCount = restartCountValue switch
        {
            < 0 => 0,
            > int.MaxValue => int.MaxValue,
            _ => (int)restartCountValue
        };

        return new ContainerMetric(
            Name: name,
            Image: image,
            State: state,
            MemoryUsageBytes: memoryUsageBytes,
            MemoryLimitBytes: memoryLimitBytes,
            MemoryPercent: memoryPercent,
            CpuPercent: cpuPercent,
            RestartCount: restartCount);
    }

    private async Task<string> GetApiPrefixAsync(
        CancellationToken cancellationToken)
    {
        if (_apiPrefix is not null)
        {
            return _apiPrefix;
        }

        using var document = await GetJsonAsync(
            "/version",
            cancellationToken);

        var apiVersion = GetString(
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
        using var response = await _httpClient.GetAsync(
            requestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    private static long CalculateMemoryUsage(
        JsonElement stats)
    {
        if (!stats.TryGetProperty(
                "memory_stats",
                out var memoryStats))
        {
            return 0;
        }

        var totalUsage =
            GetInt64(memoryStats, "usage");

        long cache = 0;

        if (memoryStats.TryGetProperty(
                "stats",
                out var memoryDetails))
        {
            cache = GetInt64(
                memoryDetails,
                "inactive_file");

            if (cache == 0)
            {
                cache = GetInt64(
                    memoryDetails,
                    "total_inactive_file");
            }
        }

        return Math.Max(0, totalUsage - cache);
    }

    private static double CalculateCpuPercent(
        JsonElement stats)
    {
        if (!stats.TryGetProperty(
                "cpu_stats",
                out var currentCpu) ||
            !stats.TryGetProperty(
                "precpu_stats",
                out var previousCpu))
        {
            return 0;
        }

        var currentContainerUsage =
            GetNestedInt64(
                currentCpu,
                "cpu_usage",
                "total_usage");

        var previousContainerUsage =
            GetNestedInt64(
                previousCpu,
                "cpu_usage",
                "total_usage");

        var currentSystemUsage =
            GetInt64(
                currentCpu,
                "system_cpu_usage");

        var previousSystemUsage =
            GetInt64(
                previousCpu,
                "system_cpu_usage");

        var containerDelta =
            currentContainerUsage -
            previousContainerUsage;

        var systemDelta =
            currentSystemUsage -
            previousSystemUsage;

        if (containerDelta <= 0 || systemDelta <= 0)
        {
            return 0;
        }

        var onlineCpuCount =
            GetInt64(currentCpu, "online_cpus");

        if (onlineCpuCount <= 0 &&
            currentCpu.TryGetProperty(
                "cpu_usage",
                out var cpuUsage) &&
            cpuUsage.TryGetProperty(
                "percpu_usage",
                out var perCpuUsage) &&
            perCpuUsage.ValueKind == JsonValueKind.Array)
        {
            onlineCpuCount =
                perCpuUsage.GetArrayLength();
        }

        onlineCpuCount = Math.Max(
            1,
            onlineCpuCount);

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
                out var names) &&
            names.ValueKind == JsonValueKind.Array)
        {
            foreach (var nameElement in
                     names.EnumerateArray())
            {
                var name = nameElement.GetString();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name.TrimStart('/');
                }
            }
        }

        var id = GetString(container, "Id");

        return id.Length > 12
            ? id[..12]
            : id;
    }

    private static string GetString(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(
                   propertyName,
                   out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static long GetInt64(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property))
        {
            return 0;
        }

        return property.TryGetInt64(out var value)
            ? value
            : 0;
    }

    private static long GetNestedInt64(
        JsonElement element,
        string parentProperty,
        string childProperty)
    {
        if (!element.TryGetProperty(
                parentProperty,
                out var parent))
        {
            return 0;
        }

        return GetInt64(
            parent,
            childProperty);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}