using System.Text;
using System.Text.RegularExpressions;

namespace MissionControl.Agent.Endpoints;

internal static class PublicDiagnosticSanitizer
{
    internal const int MaximumErrorLength = 240;
    internal const int MaximumEndpointLength = 200;

    private static readonly Regex ExceptionPrefix = new(
        @"^(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*Exception(?:\s*\([^)]*\))?\s*:\s*",
        RegexOptions.CultureInvariant);

    private static readonly Regex UriCredentials = new(
        @"\b([A-Za-z][A-Za-z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@",
        RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveAssignment = new(
        @"\b(password|pwd|token|api[-_]?key|secret|user\s*id|username|connection\s*string|connectionstring)\s*[:=]\s*[^;\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BearerCredential = new(
        @"\bBearer\s+[^\s;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DockerSocketPath = new(
        @"(?:/var/run/docker\.sock|\\\\\.\\pipe\\docker_engine)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? SanitizeError(
        string? diagnostic,
        bool succeeded)
    {
        if (succeeded || string.IsNullOrWhiteSpace(diagnostic))
        {
            return null;
        }

        string sanitized = NormalizeSingleLine(diagnostic);
        sanitized = ExceptionPrefix.Replace(sanitized, string.Empty);
        sanitized = RedactSensitiveText(sanitized);

        return Limit(sanitized, MaximumErrorLength);
    }

    public static string? SanitizeEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        string sanitized = RedactSensitiveText(
            NormalizeSingleLine(endpoint));

        return Limit(sanitized, MaximumEndpointLength);
    }

    private static string NormalizeSingleLine(string value)
    {
        var result = new StringBuilder(
            Math.Min(value.Length, 1_024));
        bool previousWasSpace = false;

        foreach (char character in value)
        {
            if (character is '\r' or '\n')
            {
                break;
            }

            bool isSpace =
                char.IsWhiteSpace(character) ||
                char.IsControl(character);

            if (isSpace)
            {
                if (!previousWasSpace && result.Length > 0)
                {
                    result.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            result.Append(character);
            previousWasSpace = false;

            if (result.Length >= 1_024)
            {
                break;
            }
        }

        return result.ToString().Trim();
    }

    private static string RedactSensitiveText(string value)
    {
        string sanitized = UriCredentials.Replace(
            value,
            "$1[redacted]@");
        sanitized = SensitiveAssignment.Replace(
            sanitized,
            "$1=[redacted]");
        sanitized = BearerCredential.Replace(
            sanitized,
            "Bearer [redacted]");

        return DockerSocketPath.Replace(
            sanitized,
            "[redacted Docker endpoint]");
    }

    private static string? Limit(
        string value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maximumLength
            ? value
            : $"{value[..(maximumLength - 1)].TrimEnd()}…";
    }
}
