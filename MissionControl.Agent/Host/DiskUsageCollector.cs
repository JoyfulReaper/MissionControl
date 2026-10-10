using Microsoft.Extensions.Options;
using MissionControl.Contracts.Agent;

namespace MissionControl.Agent.Host;

internal interface IDiskUsageCollector
{
    Task<DiskCollectionResult> CollectAsync(CancellationToken cancellationToken);
}

internal sealed record DiskCollectionResult(IReadOnlyList<PublicDiskUsage>? Disks, string? Error);
internal sealed record DiskSpace(long Total, long Free, long Available, string FileSystem);

internal interface IDiskMetricsSource
{
    bool IsLinux { get; }
    bool IsContainer { get; }
    Task<string> ReadMountInfoAsync(string path, CancellationToken cancellationToken);
    DiskSpace GetSpace(string path);
}

internal sealed class DiskMetricsSource : IDiskMetricsSource
{
    public bool IsLinux => OperatingSystem.IsLinux();
    public bool IsContainer => string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
        || File.Exists("/.dockerenv") || File.Exists("/run/.containerenv");
    public Task<string> ReadMountInfoAsync(string path, CancellationToken cancellationToken) => File.ReadAllTextAsync(path, cancellationToken);
    public DiskSpace GetSpace(string path)
    {
        var drive = new DriveInfo(path);
        return new(drive.TotalSize, drive.TotalFreeSpace, drive.AvailableFreeSpace, drive.DriveFormat);
    }
}

internal sealed class DiskUsageCollector(IDiskMetricsSource source, IOptions<AgentOptions> options) : IDiskUsageCollector
{
    internal const string ReadError = "Disk usage could not be read for one or more local filesystems.";
    private static readonly HashSet<string> LocalFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "ext2", "ext3", "ext4", "xfs", "btrfs", "zfs", "f2fs", "jfs", "reiserfs", "bcachefs", "vfat", "exfat", "ntfs", "ntfs3", "ufs"
    };

    public async Task<DiskCollectionResult> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!source.IsLinux) return new(null, null);
        DiskOptions settings = options.Value.Disks;
        bool hostRootConfigured = !string.IsNullOrWhiteSpace(settings.HostRootPath);
        if ((source.IsContainer && !hostRootConfigured) ||
            (hostRootConfigured && (!Path.IsPathFullyQualified(settings.HostRootPath!) ||
                Path.GetFullPath(settings.HostRootPath!) == Path.GetPathRoot(Path.GetFullPath(settings.HostRootPath!)) ||
                settings.MountInfoPath == "/proc/self/mountinfo")))
        {
            return new([], "Host disk visibility requires a host root bind and a host mount table.");
        }

        string contents = await source.ReadMountInfoAsync(settings.MountInfoPath, cancellationToken);
        Mount[] mounts = ParseMounts(contents).ToArray();
        if (mounts.Length == 0) throw new InvalidDataException("The mount table is empty.");
        if (mounts.Any(mount => mount.MountPoint == "/" && mount.FileSystem == "overlay"))
            return new([], "Host disk visibility requires a host root bind and a host mount table.");

        Mount[] visibleMounts = hostRootConfigured
            ? ParseMounts(await source.ReadMountInfoAsync("/proc/self/mountinfo", cancellationToken)).ToArray()
            : [];

        var disks = new List<PublicDiskUsage>();
        string? error = null;
        foreach (Mount mount in mounts.Where(IsCapacityMount).OrderBy(mount => mount.MountPoint.Length).DistinctBy(mount => mount.Device))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string path = hostRootConfigured ? ResolveHostPath(settings.HostRootPath!, mount.MountPoint) : mount.MountPoint;
                if (hostRootConfigured)
                {
                    Mount? visible = visibleMounts.Where(candidate => path == candidate.MountPoint ||
                            path.StartsWith(candidate.MountPoint.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        .OrderByDescending(candidate => candidate.MountPoint.Length).FirstOrDefault();
                    if (visible?.Device != mount.Device)
                        throw new IOException("The host filesystem is not visible at the configured path.");
                }
                DiskSpace space = source.GetSpace(path);
                // A missing bind must never fall through to the container's overlay filesystem.
                if (NormalizeFormat(space.FileSystem) != NormalizeFormat(mount.FileSystem))
                    throw new IOException("The visible filesystem is not a local capacity filesystem.");
                if (space.Total > 0) disks.Add(Calculate(mount.MountPoint, mount.FileSystem, space));
            }
            catch (Exception exception) when (IsReadFailure(exception))
            {
                error = ReadError;
            }
        }

        return new(disks, error);
    }

    internal static bool IsReadFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException;

    internal static PublicDiskUsage Calculate(string mountPoint, string fileSystem, DiskSpace space)
    {
        long total = Math.Max(0, space.Total);
        long free = Math.Clamp(space.Free, 0, total);
        long available = Math.Clamp(space.Available, 0, free);
        long used = total - free;
        return new(mountPoint, total, used, free, available, total > 0 ? (double)used / total * 100 : 0, fileSystem);
    }

    internal static string ResolveHostPath(string root, string mountPoint)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(Path.Combine(normalizedRoot, mountPoint.TrimStart('/')));
        if (resolved != normalizedRoot && !resolved.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("Mount point escapes the configured host root.");
        return resolved;
    }

    internal sealed record Mount(string Device, string Root, string MountPoint, string FileSystem);

    internal static IEnumerable<Mount> ParseMounts(string contents)
    {
        foreach (string line in contents.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] sides = line.Split(" - ", 2, StringSplitOptions.None);
            string[] fields = sides[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] tail = sides.Length == 2 ? sides[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
            if (fields.Length < 6 || tail.Length < 3) throw new InvalidDataException("Invalid mount information.");
            yield return new(fields[2], Unescape(fields[3]), Unescape(fields[4]), tail[0]);
        }
    }

    private static string NormalizeFormat(string format) => format.ToLowerInvariant() switch
    {
        "ext" or "ext2" or "ext3" or "ext4" or "ext2/ext3" or "ext2/ext3/ext4" => "ext",
        "msdos" or "vfat" => "vfat",
        "ntfs3" or "ntfs" => "ntfs",
        var value => value
    };

    internal static bool IsCapacityMount(Mount mount) => LocalFormats.Contains(mount.FileSystem)
        && (mount.Root == "/" || mount.FileSystem == "btrfs") && mount.MountPoint.StartsWith('/')
        && !mount.MountPoint.StartsWith("/proc/", StringComparison.Ordinal)
        && !mount.MountPoint.StartsWith("/sys/", StringComparison.Ordinal)
        && !mount.MountPoint.StartsWith("/dev/", StringComparison.Ordinal);

    private static string Unescape(string value) => value.Replace("\\040", " ").Replace("\\011", "\t").Replace("\\012", "\n").Replace("\\134", "\\");
}
