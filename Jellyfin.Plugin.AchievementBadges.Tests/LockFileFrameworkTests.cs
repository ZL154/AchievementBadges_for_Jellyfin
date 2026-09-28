using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// Every project that multi-targets has to keep both frameworks in its lock
/// file. Dependabot regenerates these from a single framework, which drops the
/// net10.0 section - the Jellyfin 12 half of the matrix - while the csproj
/// still names both. It happened on #135 and again on #145, and both times the
/// checks were green: NuGet's locked mode tolerates a lock file that carries
/// fewer frameworks than its project, so nothing failed until someone ran a
/// locked restore by hand. This is that missing check.
/// </summary>
public class LockFileFrameworkTests
{
    [Theory]
    [InlineData("Jellyfin.Plugin.AchievementBadges")]
    [InlineData("Jellyfin.Plugin.AchievementBadges.Tests")]
    public void AMultiTargetedProjectLocksBothFrameworks(string project)
    {
        var root = RepoRoot();
        var csproj = File.ReadAllText(Path.Combine(root, project, project + ".csproj"));
        Assert.Contains("<TargetFrameworks>net9.0;net10.0</TargetFrameworks>", csproj, StringComparison.Ordinal);

        using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, project, "packages.lock.json")));
        var locked = lockFile.RootElement.GetProperty("dependencies")
            .EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "net10.0", "net9.0" }, locked);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
