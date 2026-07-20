using System.Runtime.CompilerServices;
using Xunit;

namespace MissionControl.Tests;

public sealed class GitActivitySourceStructureTests
{
    [Fact]
    public void HostPagesUseSharedGitActivityComponent()
    {
        string dashboardPage = ReadRepositoryFile(
            "MissionControl.Dashboard/Components/Pages/GitActivity.razor");
        string mobilePage = ReadRepositoryFile(
            "MissionControl.Mobile/Components/Pages/GitActivity.razor");
        string sharedPage = ReadRepositoryFile(
            "MissionControl.UI/Components/GitActivity/GitActivityPageContent.razor");

        Assert.Contains("@page \"/gitactivity\"", dashboardPage);
        Assert.Contains("@page \"/gitactivity\"", mobilePage);
        Assert.Contains("<GitActivityPageContent", dashboardPage);
        Assert.Contains("<GitActivityPageContent", mobilePage);
        Assert.DoesNotContain("<article", dashboardPage);
        Assert.DoesNotContain("<article", mobilePage);
        Assert.Contains("<article", sharedPage);
    }

    [Fact]
    public void DashboardNavigationHasExactOrderAndNoSettings()
    {
        string navigation = ReadRepositoryFile(
            "MissionControl.Dashboard/Components/Layout/NavMenu.razor");

        AssertInOrder(
            navigation,
            "Overview",
            "Events",
            "Services",
            "Git Activity");
        Assert.DoesNotContain("Settings", navigation);
        Assert.Contains("href=\"events\"", navigation);
        Assert.Contains("href=\"gitactivity\"", navigation);
        Assert.True(
            navigation.IndexOf("</nav>", StringComparison.Ordinal) <
            navigation.IndexOf("<footer", StringComparison.Ordinal));
    }

    [Fact]
    public void MobileNavigationHasExactOrderAndUsesDashboardProxyClient()
    {
        string navigation = ReadRepositoryFile(
            "MissionControl.Mobile/Components/Layout/NavMenu.razor");
        string registration = ReadRepositoryFile(
            "MissionControl.Mobile/MauiProgram.cs");

        AssertInOrder(
            navigation,
            "<strong>Overview</strong>",
            "<strong>Events</strong>",
            "<strong>Services</strong>",
            "<strong>Git Activity</strong>",
            "<strong>Settings</strong>");
        Assert.Contains("href=\"gitactivity\"", navigation);
        Assert.Contains("api/mobile/git-activity", registration);
        Assert.Contains("MobileApiAuthorizationHandler", registration);
        Assert.DoesNotContain("api/github/activity", registration);
    }

    private static void AssertInOrder(
        string value,
        params string[] expected)
    {
        int previousIndex = -1;

        foreach (string item in expected)
        {
            int index = value.IndexOf(
                item,
                previousIndex + 1,
                StringComparison.Ordinal);

            Assert.True(
                index > previousIndex,
                $"Expected '{item}' after index {previousIndex}.");

            previousIndex = index;
        }
    }

    private static string ReadRepositoryFile(
        string relativePath,
        [CallerFilePath] string testFilePath = "")
    {
        string repositoryRoot = Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(testFilePath)!,
                ".."));

        return File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));
    }
}
