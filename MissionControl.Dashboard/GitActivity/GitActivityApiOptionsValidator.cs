using Microsoft.Extensions.Options;

namespace MissionControl.Dashboard.GitActivity;

internal sealed class GitActivityApiOptionsValidator
    : IValidateOptions<GitActivityApiOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        GitActivityApiOptions options)
    {
        List<string> failures = [];

        if (!Uri.TryCreate(
                options.BaseUrl,
                UriKind.Absolute,
                out Uri? baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
        {
            failures.Add(
                "GitActivityApi:BaseUrl must be an absolute HTTP or HTTPS URI.");
        }

        if (options.Enabled &&
            string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add(
                "GitActivityApi:ApiKey is required when Git Activity integration is enabled.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
