using System.Globalization;
using MissionControl.Agent.Models;

namespace MissionControl.Agent.Host;

internal sealed class HostMetricsCollector : IHostMetricsCollector
{
    private static readonly TimeSpan CpuSampleDuration =
        TimeSpan.FromMilliseconds(250);

    public async Task<HostMetric> GetMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return new HostMetric(
                LogicalProcessorCount: Environment.ProcessorCount,
                CpuPercent: null,
                MemoryTotalBytes: null,
                MemoryAvailableBytes: null);
        }

        string firstStat =
            await File.ReadAllTextAsync(
                "/proc/stat",
                cancellationToken);

        await Task.Delay(
            CpuSampleDuration,
            cancellationToken);

        Task<string> statTask =
            File.ReadAllTextAsync(
                "/proc/stat",
                cancellationToken);
        Task<string> memoryTask =
            File.ReadAllTextAsync(
                "/proc/meminfo",
                cancellationToken);

        await Task.WhenAll(statTask, memoryTask);

        CpuSample firstCpu = ParseCpuSample(firstStat);
        CpuSample secondCpu = ParseCpuSample(await statTask);
        (long totalBytes, long availableBytes) =
            ParseMemory(await memoryTask);

        return new HostMetric(
            LogicalProcessorCount:
                CountLogicalProcessors(await statTask),
            CpuPercent:
                CalculateCpuPercent(firstCpu, secondCpu),
            MemoryTotalBytes:
                totalBytes,
            MemoryAvailableBytes:
                availableBytes);
    }

    internal static CpuSample ParseCpuSample(string contents)
    {
        string? aggregateLine = contents
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("cpu ", StringComparison.Ordinal));

        if (aggregateLine is null)
        {
            throw new InvalidDataException(
                "The host CPU statistics did not contain an aggregate sample.");
        }

        ulong[] values = aggregateLine
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(value => ulong.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();

        if (values.Length < 4)
        {
            throw new InvalidDataException(
                "The host CPU aggregate sample was incomplete.");
        }

        ulong total = 0;

        foreach (ulong value in values)
        {
            total += value;
        }

        ulong idle = values[3] + (values.Length > 4 ? values[4] : 0);

        return new CpuSample(total, idle);
    }

    internal static double CalculateCpuPercent(
        CpuSample first,
        CpuSample second)
    {
        ulong totalDelta = second.Total >= first.Total
            ? second.Total - first.Total
            : 0;
        ulong idleDelta = second.Idle >= first.Idle
            ? second.Idle - first.Idle
            : 0;

        if (totalDelta == 0)
        {
            return 0;
        }

        double busyDelta = totalDelta > idleDelta
            ? totalDelta - idleDelta
            : 0;

        return Math.Clamp(
            busyDelta / totalDelta * 100.0,
            0,
            100);
    }

    internal static int CountLogicalProcessors(string contents)
    {
        int count = contents
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line =>
                line.Length > 3 &&
                line.StartsWith("cpu", StringComparison.Ordinal) &&
                char.IsDigit(line[3]));

        return count > 0
            ? count
            : Environment.ProcessorCount;
    }

    internal static (long TotalBytes, long AvailableBytes) ParseMemory(
        string contents)
    {
        Dictionary<string, long> values = contents
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(
                ':',
                2,
                StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => parts[0],
                parts => ParseKilobytes(parts[1]),
                StringComparer.Ordinal);

        if (!values.TryGetValue("MemTotal", out long totalBytes) ||
            !values.TryGetValue("MemAvailable", out long availableBytes))
        {
            throw new InvalidDataException(
                "The host memory statistics were incomplete.");
        }

        return (
            totalBytes,
            Math.Clamp(availableBytes, 0, totalBytes));
    }

    private static long ParseKilobytes(string value)
    {
        string number = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        return checked(
            long.Parse(number, CultureInfo.InvariantCulture) * 1024);
    }

    internal readonly record struct CpuSample(
        ulong Total,
        ulong Idle);
}
