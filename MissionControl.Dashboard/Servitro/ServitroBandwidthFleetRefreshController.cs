using Microsoft.Extensions.Options;
using MissionControl.Dashboard.Bandwidth;

namespace MissionControl.Dashboard.Servitro;

internal sealed class ServitroBandwidthFleetRefreshController(
    ServitroBandwidthClient client,
    IOptions<ServitroOptions> options)
    : BandwidthFleetRefreshController(
        client,
        options.Value.Enabled ? options.Value.Servers.Select(server => server.NodeId) : [],
        "Servitro");
