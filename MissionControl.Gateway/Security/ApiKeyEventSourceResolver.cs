using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace MissionControl.Gateway.Security;

public sealed class ApiKeyEventSourceResolver
    : IEventSourceResolver
{
    private readonly IReadOnlyList<EventSourceEntry> _sources;

    public ApiKeyEventSourceResolver(
        IOptions<EventSourceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _sources = options.Value.Sources
            .Select(source => new EventSourceEntry(
                source.Name,
                Encoding.UTF8.GetBytes(source.ApiKey)))
            .ToArray();
    }

    public bool TryResolve(
        string? apiKey,
        out string source)
    {
        source = string.Empty;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        var providedKeyBytes =
            Encoding.UTF8.GetBytes(apiKey);

        foreach (var configuredSource in _sources)
        {
            if (providedKeyBytes.Length !=
                configuredSource.ApiKeyBytes.Length)
            {
                continue;
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    providedKeyBytes,
                    configuredSource.ApiKeyBytes))
            {
                continue;
            }

            source = configuredSource.Source;
            return true;
        }

        return false;
    }

    private sealed record EventSourceEntry(
        string Source,
        byte[] ApiKeyBytes);
}