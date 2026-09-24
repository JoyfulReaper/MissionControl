using Microsoft.Extensions.Options;

namespace MissionControl.Dashboard.Agents;

internal sealed class AgentNodesOptionsValidator : IValidateOptions<AgentNodesOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        AgentNodesOptions options)
    {
        if (options.Nodes.Count == 0)
        {
            return ValidateOptionsResult.Fail("At least one Agent node must be configured.");
        }

        if (options.Nodes.Any(node =>
                string.IsNullOrWhiteSpace(node.NodeId) ||
                string.IsNullOrWhiteSpace(node.DisplayName) ||
                !IsValidHttpUrl(node.BaseUrl)))
        {
            return ValidateOptionsResult.Fail(
                "Every Agent node must have a NodeId, display name, and absolute HTTP or HTTPS BaseUrl.");
        }

        if (options.Nodes
            .GroupBy(
                node => node.NodeId,
                StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            return ValidateOptionsResult.Fail(
                "Agent NodeIds must be unique.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsValidHttpUrl(string value)
    {
        return Uri.TryCreate(
                   value,
                   UriKind.Absolute,
                   out Uri? uri) &&
               (uri.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeHttps);
    }
}