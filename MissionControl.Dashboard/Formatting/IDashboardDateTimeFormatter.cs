/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Dashboard.Formatting;

public interface IDashboardDateTimeFormatter
{
    string Format(DateTimeOffset timestamp);

    string FormatRelative(DateTimeOffset timestamp);
}