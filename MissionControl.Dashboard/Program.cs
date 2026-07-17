using MissionControl.Dashboard.Authentication;
using MissionControl.Dashboard.Configuration;
using MissionControl.Dashboard.DependencyInjection;
using MissionControl.Dashboard.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddDashboardConfiguration();

builder.Services.AddMissionControlDashboard(
    builder.Configuration);

var app = builder.Build();

int? commandExitCode =
    await DashboardUserCommand.TryRunAsync(
        args,
        app.Services);

if (commandExitCode is int exitCode)
{
    Environment.ExitCode = exitCode;
    return;
}

app.UseMissionControlDashboard();

app.Run();
