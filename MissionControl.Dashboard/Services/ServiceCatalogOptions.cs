using MissionControl.Contracts.Services;

namespace MissionControl.Dashboard.Services;

public sealed class ServiceCatalogOptions
{
    public const string SectionName =
        "ServiceCatalog";

    public List<ServiceDefinition> Services
    {
        get;
        set;
    } = [];
}