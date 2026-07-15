using MissionControl.Agent.DependencyInjection;

var builder =
    WebApplication.CreateBuilder(args);

builder.Services.AddMissionControlAgent(
    builder.Configuration);

builder.Services.AddAgentSnapshotStorage(
    builder.Configuration);

var app = builder.Build();

app.MapGet(
    "/health/live",
    () => Results.NoContent());

app.Run();
