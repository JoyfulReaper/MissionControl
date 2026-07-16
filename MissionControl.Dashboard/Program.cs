using MissionControl.Dashboard.Configuration;
using MissionControl.Dashboard.DependencyInjection;
using MissionControl.Dashboard.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddDashboardConfiguration();

builder.Services.AddMissionControlDashboard(
    builder.Configuration);

var app = builder.Build();

app.UseMissionControlDashboard();

app.Run();
