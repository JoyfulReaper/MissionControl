/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

using Microsoft.Extensions.Options;
using MissionControl.Contracts.GitActivity;
using MissionControl.GitActivity.Storage;
using System.Security.Cryptography;
using System.Text;

namespace MissionControl.GitActivity.Endpoints;

public static class GitActivityEndpointRouteBuilderExtensions
{
    private const string ApiKeyHeaderName =
        "X-Mission-Control-Key";

    public static RouteHandlerBuilder MapGitActivityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        return endpoints
            .MapGet(
                "/api/github/activity",
                HandleGetRecentActivityAsync)
            .WithName("GetRecentGitHubActivity")
            .WithTags("GitHub Activity")
            .Produces<IReadOnlyList<GitActivityItem>>(
                StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> HandleGetRecentActivityAsync(
        HttpRequest request,
        int? limit,
        IGitActivityRepository repository,
        IOptions<GitActivityOptions> optionsAccessor,
        CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;

        if (!request.Headers.TryGetValue(
                ApiKeyHeaderName,
                out var apiKeyValues) ||
            apiKeyValues.Count != 1 ||
            !IsValidApiKey(
                apiKeyValues[0] ?? string.Empty,
                options.ApiKey))
        {
            return Results.Unauthorized();
        }

        var effectiveLimit = Math.Clamp(
            limit ?? options.DefaultResultLimit,
            1,
            options.MaxResultLimit);

        var activity = await repository.GetRecentAsync(
            effectiveLimit,
            cancellationToken);

        return Results.Ok(activity);
    }

    private static bool IsValidApiKey(
        string suppliedApiKey,
        string expectedApiKey)
    {
        if (string.IsNullOrWhiteSpace(suppliedApiKey))
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(suppliedApiKey));

        var expectedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(expectedApiKey));

        return CryptographicOperations.FixedTimeEquals(
            suppliedHash,
            expectedHash);
    }
}
