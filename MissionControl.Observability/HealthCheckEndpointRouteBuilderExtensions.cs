/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;

namespace MissionControl.Observability;

public static class HealthCheckEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapMissionControlHealthChecks(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(
            "/health/live",
            new HealthCheckOptions
            {
                Predicate = _ => false
            });

        endpoints.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions
            {
                Predicate = registration =>
                    registration.Tags.Contains("ready")
            });

        return endpoints;
    }
}
