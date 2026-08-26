using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class WorkspaceNavigationContractTests
{
    [Fact]
    public void Workspace_navigation_groups_destinations_in_user_workflow_order()
    {
        var layout = ReadSource("src", "ApplyWise.Web", "Views", "Shared", "_Layout.cshtml");

        AssertBefore(layout, ">Track</span>", ">Resume</span>");
        AssertBefore(layout, ">Resume</span>", ">Account</span>");
        AssertBefore(layout, ">Applications</a>", ">Imports</a>");
        Assert.DoesNotContain(">Interviews</a>", layout, StringComparison.Ordinal);
        AssertBefore(layout, ">Resume library</a>", ">Resume builder</a>");
        AssertBefore(layout, ">Profile</a>", ">Settings</a>");
    }

    [Fact]
    public void Workspace_keeps_the_primary_tracking_action_and_privacy_context_visible()
    {
        var layout = ReadSource("src", "ApplyWise.Web", "Views", "Shared", "_Layout.cshtml");

        Assert.Contains("sidebar-primary-action", layout, StringComparison.Ordinal);
        Assert.Contains(">Add application</span>", layout, StringComparison.Ordinal);
        Assert.Contains("Private workspace", layout, StringComparison.Ordinal);
        Assert.Contains("job-search data stay tied to your account", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Mobile_sidebar_keeps_focus_inside_the_open_navigation()
    {
        var script = ReadSource("src", "ApplyWise.Web", "wwwroot", "js", "site.js");

        Assert.Contains("event.key === 'Tab'", script, StringComparison.Ordinal);
        Assert.Contains("document.body.classList.contains('sidebar-open')", script, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault();", script, StringComparison.Ordinal);
        Assert.Contains("last.focus();", script, StringComparison.Ordinal);
        Assert.Contains("first.focus();", script, StringComparison.Ordinal);
    }

    private static void AssertBefore(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Expected to find '{first}'.");
        Assert.True(secondIndex >= 0, $"Expected to find '{second}'.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
    }

    private static string ReadSource(params string[] relativePath) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. relativePath]));

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ApplyWise.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate ApplyWise.sln above '{AppContext.BaseDirectory}'.");
    }
}
