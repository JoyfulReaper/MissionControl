/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using MissionControl.Gateway.DependencyInjection;
using MissionControl.Gateway.Endpoints;
using MissionControl.Gateway.Integrations.GitHub;
using MissionControl.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddMissionControlGateway(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "Mission Control Gateway");
app.MapGitHubWebhook();
app.MapEventPublishingEndpoints();
app.MapMissionControlHealthChecks();

app.Run();

public partial class Program;
