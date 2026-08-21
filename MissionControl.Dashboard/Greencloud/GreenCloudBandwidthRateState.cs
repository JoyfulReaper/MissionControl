namespace MissionControl.Dashboard.GreenCloud;

public sealed class GreenCloudBandwidthRateState
{
    private readonly object _sync = new();

    private double? _previousRx;
    private double? _previousTx;
    private DateTimeOffset? _previousTime;

    public (
        double? RxBytesPerSecond,
        double? TxBytesPerSecond)
        Update(double rx, double tx, DateTimeOffset now)
    {
        lock (_sync)
        {
            double? rxRate = null;
            double? txRate = null;

            if (_previousTime is DateTimeOffset previousTime &&
                _previousRx is double previousRx &&
                _previousTx is double previousTx)
            {
                double seconds = (now - previousTime).TotalSeconds;

                if (seconds > 0)
                {
                    double rxDifference = rx - previousRx;
                    double txDifference = tx - previousTx;

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

            _previousRx = rx;
            _previousTx = tx;
            _previousTime = now;

            return (rxRate, txRate);
        }
    }
}