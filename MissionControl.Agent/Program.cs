using MissionControl.Agent;
using MissionControl.Agent.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<AgentWorker>();

builder.Services.AddMissionControlAgent(
    builder.Configuration);

var host = builder.Build();
host.Run();
