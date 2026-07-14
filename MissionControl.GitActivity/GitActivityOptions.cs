/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.GitActivity;

public sealed class GitActivityOptions
{
    public const string SectionName = "GitActivity";

    public string DatabaseFileName { get; init; } =
        "git-activity.db";

    public string? BasePath { get; init; }

    public int DefaultResultLimit { get; init; } = 10;

    public int MaxResultLimit { get; init; } = 50;

    public required string ApiKey { get; init; }

    public string[] AllowedRepositories { get; init; } = [];

    public string[] AllowedBranches { get; init; } = ["main"];
}