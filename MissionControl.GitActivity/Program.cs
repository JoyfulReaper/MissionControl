using MissionControl.GitActivity.DependencyInjection;
using MissionControl.GitActivity.Endpoints;
using MissionControl.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddGitActivity(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "Mission Control Git Activity");
app.MapGitActivityEndpoints();
app.MapMissionControlHealthChecks();

app.Run();

public partial class Program;
