using MissionControl.Agent.DependencyInjection;

var builder =
    WebApplication.CreateBuilder(args);

builder.Services.AddMissionControlAgent(
    builder.Configuration);

builder.Services.AddAgentSnapshotStorage(
    builder.Configuration);

builder.Services.AddAgentApi(
    builder.Configuration);

var app = builder.Build();

app.MapGet(
    "/health/live",
    () => Results.NoContent());

app.Run();
