using MissionControl.Archive.DependencyInjection;
using MissionControl.Archive.Endpoints;
using MissionControl.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMissionControlArchive(builder.Configuration);

var app = builder.Build();

app.MapArchiveEndpoints();
app.MapMissionControlHealthChecks();

app.Run();
