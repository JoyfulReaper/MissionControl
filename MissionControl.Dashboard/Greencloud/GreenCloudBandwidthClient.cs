using Microsoft.Extensions.Options;
using MissionControl.Client.Infrastructure;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MissionControl.Dashboard.GreenCloud;

public sealed class GreenCloudBandwidthClient(
    HttpClient httpClient,
    IOptions<GreenCloudOptions> options,
    GreenCloudBandwidthRateState rateState,
    TimeProvider timeProvider)
    : IBandwidthUsageClient,
      IGreenCloudBandwidthFleetClient
{
    private readonly GreenCloudOptions _options = options.Value;

    public Task<BandwidthUsageSnapshot> GetAsync(
    CancellationToken cancellationToken = default)
    {
        GreenCloudServerOptions server =
            GetLegacyServer();

        return GetServerAsync(
            server.NodeId,
            server.ServerId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<GreenCloudBandwidthNodeResult>>
        GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return [];
        }

        Task<GreenCloudBandwidthNodeResult>[] tasks =
            _options.Servers
                .Select(server => GetNodeAsync(server, cancellationToken))
                .ToArray();

        return await Task.WhenAll(tasks);
    }

    private async Task<GreenCloudBandwidthNodeResult>
        GetNodeAsync(
            GreenCloudServerOptions server,
            CancellationToken cancellationToken)
    {
        try
        {
            BandwidthUsageSnapshot snapshot =
                await GetServerAsync(
                    server.NodeId,
                    server.ServerId,
                    cancellationToken);

            return new GreenCloudBandwidthNodeResult(
                server.NodeId,
                snapshot,
                Error: null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                HttpRequestException or
                TaskCanceledException or
                InvalidOperationException or
                System.Text.Json.JsonException)
        {
            return new GreenCloudBandwidthNodeResult(
                server.NodeId,
                Snapshot: null,
                Error: exception.Message);
        }
    }

    private GreenCloudServerOptions GetLegacyServer()
    {
        if (!string.IsNullOrWhiteSpace(_options.ServerId))
        {
            GreenCloudServerOptions? configured =
                _options.Servers.FirstOrDefault(
                    server =>
                        string.Equals(
                            server.ServerId,
                            _options.ServerId,
                            StringComparison.OrdinalIgnoreCase));

            return configured ??
                new GreenCloudServerOptions
                {
                    NodeId = _options.ServerId,
                    ServerId = _options.ServerId
                };
        }

        GreenCloudServerOptions? first = _options.Servers.FirstOrDefault();

        return first ??
            throw new InvalidOperationException(
                "No GreenCloud server is configured.");
    }

    private async Task<BandwidthUsageSnapshot> GetServerAsync(
    string nodeId,
    string serverId,
    CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                $"api/server/" +
                $"{Uri.EscapeDataString(serverId)}" +
                "?state=true");

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", _options.ApiToken);

        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                "application/json"));

        using HttpResponseMessage response =
            await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        GreenCloudServerResponse? payload =
            await response.Content
                .ReadFromJsonAsync<GreenCloudServerResponse>(cancellationToken);

        GreenCloudServer server =
            payload?.Data ??
            throw new InvalidOperationException("GreenCloud returned an empty response.");

        double limit = ParseLimit(server.Network.Primary.Limit);
        double rx = server.State.Network.Primary.Traffic.Rx;
        double tx = server.State.Network.Primary.Traffic.Tx;
        double used = server.State.Network.Primary.Traffic.Total;

        double remaining = Math.Max(limit - used, 0);

        double usedPercent = limit > 0
            ? used / limit * 100
            : 0;

        double remainingPercent = limit > 0
            ? remaining / limit * 100
            : 0;

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset periodStart = server.CurrentMonthlyPeriod.Start;
        DateTimeOffset periodEnd = server.CurrentMonthlyPeriod.End;

        double elapsedDays = Math.Max((now - periodStart).TotalDays, 0.001);
        double daysRemaining = Math.Max((periodEnd - now).TotalDays, 0);
        double averagePerDay = used / elapsedDays;

        double availablePerDay = daysRemaining > 0
            ? remaining / daysRemaining
            : 0;

        double projected = daysRemaining > 0
            ? used +
                averagePerDay * daysRemaining
            : used;

        double projectedPercent = limit > 0
            ? projected / limit * 100
            : 0;

        var rates = rateState.Update(nodeId, rx, tx, now);

        return new BandwidthUsageSnapshot(
            server.Name,
            server.State.Status,
            limit,
            rx,
            tx,
            used,
            remaining,
            usedPercent,
            remainingPercent,
            periodStart,
            periodEnd,
            elapsedDays,
            daysRemaining,
            averagePerDay,
            availablePerDay,
            projected,
            projectedPercent,
            rates.RxBytesPerSecond,
            rates.TxBytesPerSecond,
            now);
    }

    private static double ParseLimit(
        string value)
    {
        Match match =
            Regex.Match(
                value,
                @"^\s*(?<amount>[\d.]+)\s*" +
                @"(?<unit>KB|MB|GB|TB|PB)\s*$",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant);

        if (!match.Success ||
            !double.TryParse(
                match.Groups["amount"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double amount))
        {
            throw new InvalidOperationException(
                $"Unknown GreenCloud bandwidth " +
                $"limit format: '{value}'.");
        }

        double multiplier =
            match.Groups["unit"]
                .Value
                .ToUpperInvariant() switch
            {
                "KB" => 1024d,
                "MB" => 1024d * 1024,
                "GB" => 1024d * 1024 * 1024,
                "TB" => 1024d * 1024 * 1024 * 1024,
                "PB" => 1024d * 1024 * 1024 * 1024 * 1024,

                _ => throw new InvalidOperationException($"Unknown bandwidth unit in '{value}'.")
            };

        return amount * multiplier;
    }
}