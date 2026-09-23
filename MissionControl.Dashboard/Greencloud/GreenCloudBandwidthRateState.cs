namespace MissionControl.Dashboard.GreenCloud;

public sealed class GreenCloudBandwidthRateState
{
    private readonly object _sync = new();

    private readonly Dictionary<string, RateSample> _previousByServer = new(StringComparer.OrdinalIgnoreCase);

    public (
        double? RxBytesPerSecond,
        double? TxBytesPerSecond)
        Update(
            string serverKey,
            double rx,
            double tx,
            DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverKey);

        lock (_sync)
        {
            double? rxRate = null;
            double? txRate = null;

            if (_previousByServer.TryGetValue(
                    serverKey,
                    out RateSample? previous))
            {
                double seconds =
                    (now - previous.Time).TotalSeconds;

                if (seconds > 0)
                {
                    double rxDifference = rx - previous.Rx;
                    double txDifference = tx - previous.Tx;

                    if (rxDifference >= 0)
                    {
                        rxRate = rxDifference / seconds;
                    }

                    if (txDifference >= 0)
                    {
                        txRate = txDifference / seconds;
                    }
                }
            }

            _previousByServer[serverKey] =
                new RateSample(
                    rx,
                    tx,
                    now);

            return (
                rxRate,
                txRate);
        }
    }

    private sealed record RateSample(
        double Rx,
        double Tx,
        DateTimeOffset Time);
}