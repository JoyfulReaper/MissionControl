using MissionControl.Agent.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMissionControlAgent(
    builder.Configuration);

var host = builder.Build();
host.Run();
