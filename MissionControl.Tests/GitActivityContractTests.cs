using MissionControl.Contracts.GitActivity;
using System.Text.Json;
using Xunit;

namespace MissionControl.Tests;

public sealed class GitActivityContractTests
{
    [Fact]
    public void GitActivityItemRoundTripsThroughJson()
    {
        var expected = new GitActivityItem(
            Repository: "JoyfulReaper/MissionControl",
            Branch: "dev",
            Sha: "0123456789abcdef",
            Message: "Add shared Git Activity page",
            Author: "Kyle Givler",
            AuthorUsername: "JoyfulReaper",
            Timestamp: DateTimeOffset.Parse(
                "2026-07-20T18:00:00Z"),
            Url:
                "https://github.com/JoyfulReaper/MissionControl/commit/0123456789abcdef");

        string json = JsonSerializer.Serialize(expected);
        GitActivityItem? actual =
            JsonSerializer.Deserialize<GitActivityItem>(json);

        Assert.Equal(expected, actual);
    }
}
