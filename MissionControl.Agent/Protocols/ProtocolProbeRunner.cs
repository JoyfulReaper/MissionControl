using MissionControl.Agent.Models;
using System.Diagnostics;


namespace MissionControl.Agent.Protocols;

internal sealed class ProtocolProbeRunner
{
    private readonly IReadOnlyDictionary<string, IProtocolProbe> _probes;

    public ProtocolProbeRunner(IEnumerable<IProtocolProbe> probes)
    {
        var registeredProbes = probes.ToArray();
        var duplicateProbes = registeredProbes
            .GroupBy(
                probe => probe.Protocol,
                StringComparer.OrdinalIgnoreCase
            ).FirstOrDefault(group => group.Count() > 1);

        if (duplicateProbes is not null)
        {
            throw new InvalidOperationException(
                $"Multiple protocol probes are registered for " +
                $"protocol '{duplicateProbes.Key}'. ");
        }

        _probes = registeredProbes.ToDictionary(probe => probe.Protocol, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<ProtocolProbeResult>> RunAsync(
        IReadOnlyList<ProbeOptions> options,
        CancellationToken cancellationToken)
    {
        var probeTasks = options.Select(option => RunOneAsync(option, cancellationToken));
        return await Task.WhenAll(probeTasks);
    }

    private async Task<ProtocolProbeResult> RunOneAsync(
        ProbeOptions options,
        CancellationToken cancellationToken
    )
    {
        var endpoint = FormatEndpoint(options.Host, options.Port);
        if (!_probes.TryGetValue(options.Protocol, out var probe))
        {
            return new ProtocolProbeResult(
                Service: options.Name,
                Endpoint: endpoint,
                Succeeded: false,
                DurationMilliseconds: 0,
                Error: $"Unsupported protocol '{options.Protocol}'.");
        }

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(options.TimeoutMilliseconds));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await probe.ExecuteAsync(options, timeoutSource.Token);

            return new ProtocolProbeResult(
                Service: options.Name,
                Endpoint: endpoint,
                Succeeded: true,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Error: null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // The agent is shutting down. Do not convert that into
            // a failed probe result.
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ProtocolProbeResult(
                Service: options.Name,
                Endpoint: endpoint,
                Succeeded: false,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Error:
                    $"Timed out after " +
                    $"{options.TimeoutMilliseconds} milliseconds.");
        }
        catch (Exception exception)
        {
            return new ProtocolProbeResult(
                Service: options.Name,
                Endpoint: endpoint,
                Succeeded: false,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Error:
                    $"{exception.GetType().Name}: " +
                    $"{exception.Message}");
        }
    }


    private static string FormatEndpoint(
        string host,
        int port)
    {
        // Prevent an IPv6 endpoint such as ::1:7 from being ambiguous.
        return host.Contains(':')
            ? $"[{host}]:{port}"
            : $"{host}:{port}";
    }
}
