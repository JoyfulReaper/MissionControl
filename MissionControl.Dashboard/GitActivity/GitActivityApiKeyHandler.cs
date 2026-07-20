using Microsoft.Extensions.Options;

namespace MissionControl.Dashboard.GitActivity;

internal sealed class GitActivityApiKeyHandler(
    IOptions<GitActivityApiOptions> optionsAccessor)
    : DelegatingHandler
{
    internal const string HeaderName =
        "X-Mission-Control-Key";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        GitActivityApiOptions options =
            optionsAccessor.Value;

        if (!options.Enabled)
        {
            throw new InvalidOperationException(
                "Git Activity integration is disabled.");
        }

        request.Headers.Remove(HeaderName);
        request.Headers.TryAddWithoutValidation(
            HeaderName,
            options.ApiKey);

        return base.SendAsync(request, cancellationToken);
    }
}
