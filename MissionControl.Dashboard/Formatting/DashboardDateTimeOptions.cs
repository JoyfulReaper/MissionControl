/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Dashboard.Formatting;

public sealed class DashboardDateTimeOptions
{
    public const string SectionName =
        "Dashboard:DateTime";

    public string TimeZoneId { get; set; } =
        "UTC";

    public string Format { get; set; } =
        "yyyy-MM-dd HH:mm:ss zzz";
}