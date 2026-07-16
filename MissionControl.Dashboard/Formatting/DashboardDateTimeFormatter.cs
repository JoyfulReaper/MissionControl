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
    private readonly TimeZoneInfo _timeZone;
    private readonly string _format;

    public DashboardDateTimeFormatter(
        IOptions<DashboardDateTimeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        DashboardDateTimeOptions settings =
            options.Value;

        _timeZone =
            TimeZoneInfo.FindSystemTimeZoneById(
                settings.TimeZoneId);

        _format = settings.Format;
    }

    public string Format(
        DateTimeOffset timestamp)
    {
        DateTimeOffset localTimestamp =
            TimeZoneInfo.ConvertTime(
                timestamp,
                _timeZone);

        return localTimestamp.ToString(
            _format,
            CultureInfo.InvariantCulture);
    }
}