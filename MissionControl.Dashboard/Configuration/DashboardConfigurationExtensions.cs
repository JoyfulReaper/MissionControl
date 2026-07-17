namespace MissionControl.Dashboard.Configuration;

public static class DashboardConfigurationExtensions
{
    public static ConfigurationManager AddDashboardConfiguration(
        this ConfigurationManager configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.AddJsonFile(
            "services.json",
            optional: false,
            reloadOnChange: true);

        return configuration;
    }
}
