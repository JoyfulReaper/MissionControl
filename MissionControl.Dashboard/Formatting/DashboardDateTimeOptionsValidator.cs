using Microsoft.Extensions.Options;

namespace MissionControl.Dashboard.Formatting;

public sealed class DashboardDateTimeOptionsValidator
    : IValidateOptions<DashboardDateTimeOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        DashboardDateTimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.TimeZoneId))
        {
            return ValidateOptionsResult.Fail(
                "Dashboard date/time timezone is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Format))
        {
            return ValidateOptionsResult.Fail(
                "Dashboard date/time format is required.");
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(
                options.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return ValidateOptionsResult.Fail(
                "Dashboard date/time timezone is invalid.");
        }
        catch (InvalidTimeZoneException)
        {
            return ValidateOptionsResult.Fail(
                "Dashboard date/time timezone is invalid.");
        }

        return ValidateOptionsResult.Success;
    }
}
