using Microsoft.Extensions.Options;
using MissionControl.Dashboard.Bandwidth;

namespace MissionControl.Dashboard.GreenCloud;

internal sealed class GreenCloudBandwidthFleetRefreshController(
    IGreenCloudBandwidthFleetClient client,
    IOptions<GreenCloudOptions> options)
    : BandwidthFleetRefreshController(
        client,
        options.Value.Enabled ? options.Value.Servers.Select(server => server.NodeId) : [],
        "GreenCloud");
