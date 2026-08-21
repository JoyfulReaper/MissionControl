using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

namespace MissionControl.Dashboard.WorkPlanning;

internal sealed class WorkPlanningApiKeyHandler(
    IOptions<WorkPlanningApiOptions> optionsAccessor)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                options.ApiKey);

        return base.SendAsync(
            request,
            cancellationToken);
    }
}