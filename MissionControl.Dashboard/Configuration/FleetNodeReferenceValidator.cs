using Microsoft.Extensions.Options;
using MissionControl.Dashboard.Agents;
using MissionControl.Dashboard.GreenCloud;
using MissionControl.Dashboard.Servitro;

namespace MissionControl.Dashboard.Configuration;

// Use the same startup-bound fleet as AgentFleetClient, including during catalog reloads.
internal sealed class FleetNodeReferenceValidator(IOptions<AgentNodesOptions> fleet)
    : IValidateOptions<GreenCloudOptions>, IValidateOptions<ServitroOptions>
{
    public IEnumerable<string> GetErrors(IEnumerable<(string NodeId, string Source)> references)
    {
        foreach (var reference in references)
        {
            if (!fleet.Value.Nodes.Any(node => string.Equals(
                node.NodeId, reference.NodeId, StringComparison.OrdinalIgnoreCase)))
            {
                yield return $"{reference.Source} references NodeId '{reference.NodeId}', which is not configured in Agents:Nodes.";
            }
        }
    }

    public ValidateOptionsResult Validate(string? name, GreenCloudOptions options) =>
        ValidateProvider(options.Enabled, options.Servers.Select((server, index) =>
            (server.NodeId, $"GreenCloud:Servers[{index}]")));

    public ValidateOptionsResult Validate(string? name, ServitroOptions options) =>
        ValidateProvider(options.Enabled, options.Servers.Select((server, index) =>
            (server.NodeId, $"Servitro:Servers[{index}]")));

    private ValidateOptionsResult ValidateProvider(bool enabled, IEnumerable<(string NodeId, string Source)> references)
    {
        string[] errors = enabled ? GetErrors(references).ToArray() : [];
        return errors.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
