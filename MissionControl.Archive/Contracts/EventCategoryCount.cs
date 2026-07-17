/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Archive.Contracts;

public sealed record EventCategoryCount(
    string Name,
    long Count);