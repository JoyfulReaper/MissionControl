using Microsoft.AspNetCore.WebUtilities;
using MissionControl.Contracts.GitActivity;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace MissionControl.Client.GitActivity;

public sealed record GitActivityClientOptions(string RequestPath);

public sealed class GitActivityClient(
    HttpClient client,
    GitActivityClientOptions options)
    : IGitActivityClient
{
    private const int DefaultResultLimit = 25;
    private const int MaxResultLimit = 50;

    public async Task<IReadOnlyList<GitActivityItem>> GetRecentAsync(
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        int effectiveLimit = Math.Clamp(
            limit ?? DefaultResultLimit,
            1,
            MaxResultLimit);

        string requestPath = ValidateRequestPath(
            options.RequestPath);

        string requestUri = QueryHelpers.AddQueryString(
            requestPath,
            "limit",
            effectiveLimit.ToString(CultureInfo.InvariantCulture));

        using HttpResponseMessage response =
            await client.GetAsync(requestUri, cancellationToken);

        response.EnsureSuccessStatusCode();

        try
        {
            GitActivityItem[]? activity =
                await response.Content
                    .ReadFromJsonAsync<GitActivityItem[]>(
                        cancellationToken);

            return activity
                ?? throw new InvalidOperationException(
                    "The Git Activity response was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The Git Activity response was malformed.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidOperationException(
                "The Git Activity response format was not supported.",
                exception);
        }
    }

    private static string ValidateRequestPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith('/') ||
            value.StartsWith('\\') ||
            !Uri.TryCreate(value, UriKind.Relative, out _))
        {
            throw new InvalidOperationException(
                "The Git Activity request path must be relative.");
        }

        return value;
    }
}
