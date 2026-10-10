using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MissionControl.Client.Infrastructure;
using MissionControl.Dashboard.Bandwidth;

namespace MissionControl.Dashboard.Servitro;

public sealed class ServitroBandwidthClient(
    HttpClient httpClient,
    IOptions<ServitroOptions> options,
    ServitroBandwidthRateState rateState,
    TimeProvider timeProvider) : IBandwidthFleetClient
{
    public async Task<IReadOnlyList<BandwidthNodeResult>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.Enabled)
        {
            return [];
        }

        return await Task.WhenAll(options.Value.Servers.Select(
            server => GetNodeAsync(server, cancellationToken)));
    }

    private async Task<BandwidthNodeResult> GetNodeAsync(
        ServitroServerOptions server, CancellationToken cancellationToken)
    {
        try
        {
            // Bound both headers and response-body reads, including a stalled JSON response.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"server/{Uri.EscapeDataString(server.ServerId)}?state=true");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using HttpResponseMessage response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using JsonDocument payload = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            return new BandwidthNodeResult(server.NodeId, CreateSnapshot(payload.RootElement, server.NodeId), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new(server.NodeId, null, "The Servitro bandwidth request timed out.");
        }
        catch (HttpRequestException exception)
        {
            string error = exception.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Servitro rejected the API credentials.",
                HttpStatusCode.TooManyRequests => "Servitro bandwidth requests are being rate limited.",
                HttpStatusCode.ServiceUnavailable => "Servitro bandwidth is temporarily unavailable.",
                _ => "Servitro bandwidth could not be retrieved."
            };
            return new(server.NodeId, null, error);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            return new(server.NodeId, null, "Servitro returned invalid or incomplete bandwidth data.");
        }
    }

    private BandwidthUsageSnapshot CreateSnapshot(JsonElement root, string nodeId)
    {
        JsonElement data = Property(root, "data");
        JsonElement state = Property(data, "state");
        JsonElement traffic = Property(Property(Property(state, "network"), "primary"), "traffic");
        double rx = Bytes(Property(traffic, "rx"));
        double tx = Bytes(Property(traffic, "tx"));
        double limit = ParseLimit(Text(Property(Property(Property(data, "network"), "primary"), "limit")));
        JsonElement period = Property(data, "currentMonthlyPeriod");
        DateTimeOffset start = Date(Property(period, "start"));
        DateTimeOffset end = Date(Property(period, "end"));
        if (end <= start)
        {
            throw new JsonException();
        }

        string name = Text(Property(data, "name"));
        string status = Text(Property(state, "status"));
        DateTimeOffset now = timeProvider.GetUtcNow();
        // Servitro's allowance is outbound-only. RX remains visible but never contributes to quota.
        double used = tx;
        double remaining = Math.Max(limit - used, 0);
        double elapsedDays = Math.Max((now - start).TotalDays, 0.001);
        double daysRemaining = Math.Max((end - now).TotalDays, 0);
        double averagePerDay = used / elapsedDays;
        double projected = used + averagePerDay * daysRemaining;
        var rates = rateState.Update(nodeId, rx, tx, now);

        return new BandwidthUsageSnapshot(
            name, status, limit, rx, tx, used, remaining,
            used / limit * 100, remaining / limit * 100,
            start, end, elapsedDays, daysRemaining, averagePerDay,
            daysRemaining > 0 ? remaining / daysRemaining : 0,
            projected, projected / limit * 100,
            rates.RxBytesPerSecond, rates.TxBytesPerSecond, now);
    }

    internal static double ParseLimit(string value)
    {
        if (value.Length > 128)
        {
            throw new JsonException();
        }

        Match match = Regex.Match(value,
            @"^\s*(?<amount>\d+(?:\.\d+)?)\s*(?<unit>B|[KMGTPE]I?B)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (!match.Success || !double.TryParse(match.Groups["amount"].Value,
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double amount))
        {
            throw new JsonException();
        }

        string unit = match.Groups["unit"].Value.ToUpperInvariant();
        int exponent = unit == "B" ? 0 : "KMGTPE".IndexOf(unit[0]) + 1;
        // Match the provider panel and GreenCloud's binary unit convention.
        double bytes = amount * Math.Pow(1024, exponent);
        if (!double.IsFinite(bytes) || bytes <= 0 || bytes > long.MaxValue)
        {
            throw new JsonException();
        }

        return bytes;
    }

    private static JsonElement Property(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
            ? value : throw new JsonException();

    private static string Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw new JsonException();

    private static double Bytes(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double bytes) &&
        double.IsFinite(bytes) && bytes >= 0 && bytes <= long.MaxValue
            ? bytes : throw new JsonException();

    private static DateTimeOffset Date(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out DateTimeOffset date)
            ? date : throw new JsonException();
}
