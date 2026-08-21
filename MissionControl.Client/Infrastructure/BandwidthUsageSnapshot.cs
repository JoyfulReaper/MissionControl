namespace MissionControl.Client.Infrastructure;

public sealed record BandwidthUsageSnapshot(
    string ServerName,
    string Status,
    double MonthlyLimitBytes,
    double RxBytes,
    double TxBytes,
    double UsedBytes,
    double RemainingBytes,
    double UsedPercent,
    double RemainingPercent,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    double DaysElapsed,
    double DaysRemaining,
    double AverageBytesPerDay,
    double AvailableBytesPerDay,
    double ProjectedBytes,
    double ProjectedPercent,
    double? RxBytesPerSecond,
    double? TxBytesPerSecond,
    DateTimeOffset UpdatedAt);