using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionControl.Mcp;

HostApplicationBuilder builder =
    Host.CreateApplicationBuilder(args);

// MCP uses stdout for protocol traffic.
// Application logs must therefore go to stderr.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.Configure<ArchiveOptions>(
    builder.Configuration.GetSection(
        ArchiveOptions.SectionName));

builder.Services.AddHttpClient<ArchiveClient>(
    (services, client) =>
    {
        ArchiveOptions options = services
            .GetRequiredService<IOptions<ArchiveOptions>>()
            .Value;

        if (!Uri.TryCreate(
                options.BaseUrl,
                UriKind.Absolute,
                out Uri? baseUri))
        {
            throw new InvalidOperationException(
                "Archive:BaseUrl must be an absolute URI.");
        }

        client.BaseAddress = baseUri;
        client.Timeout = TimeSpan.FromSeconds(5);
    });

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();