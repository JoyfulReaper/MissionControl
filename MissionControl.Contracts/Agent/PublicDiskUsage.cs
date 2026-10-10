namespace MissionControl.Contracts.Agent;

public sealed record PublicDiskUsage(
    string MountPoint,
    long TotalBytes,
    long UsedBytes,
    long FreeBytes,
    long AvailableBytes,
    double UsagePercent,
    string FileSystem);
