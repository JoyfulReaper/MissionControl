using Microsoft.Extensions.Options;
using MissionControl.Client.Infrastructure;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace MissionControl.Dashboard.GreenCloud;

public sealed class GreenCloudBandwidthClient(
    HttpClient httpClient,
    IOptions<GreenCloudOptions> options,
    GreenCloudBandwidthRateState rateState,
    TimeProvider timeProvider) : IBandwidthUsageClient
{
    private readonly GreenCloudOptions _options = options.Value;

    public async Task<BandwidthUsageSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                $"api/server/" +
                $"{Uri.EscapeDataString(_options.ServerId)}" +
                "?state=true");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

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

        double limit =
            ParseLimit(server.Network.Primary.Limit);

        double rx = server.State.Network.Primary.Traffic.Rx;
        double tx = server.State.Network.Primary.Traffic.Tx;
        double used = server.State.Network.Primary.Traffic.Total;

        double remaining = Math.Max(limit - used, 0);

        double usedPercent =
            limit > 0
                ? used / limit * 100
                : 0;

        double remainingPercent =
            limit > 0
                ? remaining / limit * 100
                : 0;

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset periodStart = server.CurrentMonthlyPeriod.Start;
        DateTimeOffset periodEnd = server.CurrentMonthlyPeriod.End;

        double elapsedDays = Math.Max((now - periodStart).TotalDays, 0.001);
        double daysRemaining = Math.Max((periodEnd - now).TotalDays, 0);
        double averagePerDay = used / elapsedDays;

        double availablePerDay =
            daysRemaining > 0
                ? remaining / daysRemaining
                : 0;

        double projected =
            daysRemaining > 0
                ? used +
                  averagePerDay * daysRemaining
                : used;

        double projectedPercent =
            limit > 0
                ? projected / limit * 100
                : 0;

        var rates = rateState.Update(rx, tx, now);

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

                _ => throw new InvalidOperationException(
                    $"Unknown bandwidth unit in '{value}'.")
            };

        return amount * multiplier;
    }
}