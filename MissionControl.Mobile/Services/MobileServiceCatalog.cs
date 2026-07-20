using MissionControl.Contracts.Services;
using System.Text.Json;

namespace MissionControl.Mobile.Services;

public sealed class MobileServiceCatalog
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private IReadOnlyList<ServiceDefinition>? _services;

    public async Task<IReadOnlyList<ServiceDefinition>>
        GetServicesAsync()
    {
        if (_services is not null)
        {
            return _services;
        }

        await using Stream stream =
            await FileSystem.Current.OpenAppPackageFileAsync(
                "services.json");

        ServiceCatalogFile? catalog =
            await JsonSerializer.DeserializeAsync<ServiceCatalogFile>(
                stream,
                JsonOptions);

        _services = catalog?.ServiceCatalog.Services
            ?? throw new InvalidOperationException(
                "The bundled service catalog is missing or invalid.");

        return _services;
    }

    private sealed class ServiceCatalogFile
    {
        public ServiceCatalogSection ServiceCatalog
        {
            get;
            set;
        } = new();
    }

    private sealed class ServiceCatalogSection
    {
        public List<ServiceDefinition> Services
        {
            get;
            set;
        } = [];
    }
}