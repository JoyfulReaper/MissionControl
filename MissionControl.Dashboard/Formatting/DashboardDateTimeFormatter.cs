/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using Microsoft.Extensions.Options;
using System.Globalization;

namespace MissionControl.Dashboard.Formatting;

public sealed class DashboardDateTimeFormatter
    : IDashboardDateTimeFormatter
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;
    private readonly string _format;

    public DashboardDateTimeFormatter(
        IOptions<DashboardDateTimeOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        DashboardDateTimeOptions settings = options.Value;

        _timeProvider = timeProvider;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);

        _format = settings.Format;
    }

    public string Format(
        DateTimeOffset timestamp)
    {
        DateTimeOffset localTimestamp = TimeZoneInfo.ConvertTime(timestamp, _timeZone);

        return localTimestamp.ToString(_format, CultureInfo.InvariantCulture);
    }

    public string FormatRelative(
        DateTimeOffset timestamp)
    {
        TimeSpan age = _timeProvider.GetUtcNow() - timestamp;

        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return FormatElapsed(
                (int)age.TotalMinutes,
                "minute");
        }

        if (age < TimeSpan.FromHours(24))
        {
            return FormatElapsed(
                (int)age.TotalHours,
                "hour");
        }

        return Format(timestamp);
    }

    private static string FormatElapsed(
        int value,
        string unit)
    {
        string suffix =
            value == 1
                ? string.Empty
                : "s";

        return $"{value} {unit}{suffix} ago";
    }
}