using Microsoft.Extensions.Options;
using MissionControl.Contracts.Services;

namespace MissionControl.Dashboard.Services;

internal sealed class ServiceCatalogOptionsValidator
    : IValidateOptions<ServiceCatalogOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ServiceCatalogOptions options)
    {
        List<string> failures = [];

        if (options.Services is null || options.Services.Count == 0)
        {
            return ValidateOptionsResult.Fail(
                "At least one dashboard service must be configured.");
        }

        if (options.Services.Any(service => service is null))
        {
            failures.Add(
                "Dashboard service definitions must not be null.");
        }

        ServiceDefinition[] services = options.Services
            .Where(service => service is not null)
            .ToArray();

        if (services.Any(service =>
                string.IsNullOrWhiteSpace(service.Id) ||
                string.IsNullOrWhiteSpace(service.Name) ||
                string.IsNullOrWhiteSpace(service.Group) ||
                string.IsNullOrWhiteSpace(service.Summary) ||
                string.IsNullOrWhiteSpace(service.Description) ||
                string.IsNullOrWhiteSpace(service.Visibility)))
        {
            failures.Add(
                "Every dashboard service must have an ID, name, group, summary, description, and visibility.");
        }

        if (HasDuplicates(
                services.Select(service => service.Id)))
        {
            failures.Add(
                "Dashboard service IDs must be unique.");
        }

        if (HasDuplicates(
                services
                    .Select(service => service.ContainerName)
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(value))!))
        {
            failures.Add(
                "Dashboard service container names must be unique when configured.");
        }

        if (HasDuplicates(
                services
                    .Select(service => service.ProtocolServiceKey)
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(value))!))
        {
            failures.Add(
                "Dashboard protocol service keys must be unique when configured.");
        }

        if (services.Any(service =>
                service.SearchTerms is null ||
                service.SearchTerms.Any(
                    string.IsNullOrWhiteSpace)))
        {
            failures.Add(
                "Dashboard service search terms must not contain blank entries.");
        }

        if (services.Any(service =>
                !IsValidHttpUrl(service.ApplicationUrl) ||
                !IsValidHttpUrl(service.SourceUrl)))
        {
            failures.Add(
                "Dashboard service URLs must be absolute HTTP or HTTPS URLs.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool HasDuplicates(
        IEnumerable<string> values)
    {
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        return values.Any(value => !seen.Add(value));
    }

    private static bool IsValidHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return Uri.TryCreate(
                   value,
                   UriKind.Absolute,
                   out Uri? uri) &&
               (uri.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeHttps);
    }
}
