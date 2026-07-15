using MissionControl.Agent.DependencyInjection;
using MissionControl.Agent.Endpoints;

var builder =
    WebApplication.CreateBuilder(args);

builder.Services.AddMissionControlAgent(
    builder.Configuration);

builder.Services.AddAgentSnapshotStorage(
    builder.Configuration);

builder.Services.AddAgentApi(
    builder.Configuration);

var app = builder.Build();

app.UseCors();
app.UseRateLimiter();

app.MapGet(
    "/health/live",
    () => Results.NoContent());

app.MapAgentSnapshotEndpoint();

app.Run();

public partial class Program;