using MissionControl.Agent;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
host.Run();
