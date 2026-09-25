using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.AchievementBadges.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

/// <summary>
/// [issue #140, #141] The two ways a correctly installed plugin still looks
/// broken to the person who installed it: the package reports a version
/// Jellyfin cannot act on, and the browser never receives the injected shell.
/// </summary>
public class StaleShellCacheTests : IDisposable
{
    private const string Shell = "<html><head></head><body><div id=\"app\"></div></body></html>";

    public StaleShellCacheTests()
    {
        SetDiskPatched(false);
    }

    public void Dispose()
    {
        SetDiskPatched(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>The disk patch result is a static the middleware reads; set it per case.</summary>
    private static void SetDiskPatched(bool value)
    {
        typeof(WebInjectionService)
            .GetProperty(nameof(WebInjectionService.DiagIndexPatched), BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, value);
    }

    /// <summary>
    /// Stands in for Jellyfin's static file handler: answers 304 with no body
    /// when the request carries a matching validator, 200 with the shell
    /// otherwise. That is the behaviour that decides whether this middleware
    /// has anything to inject into.
    /// </summary>
    private static async Task<HttpContext> RunAsync(string? ifNoneMatch, string? ifModifiedSince = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/web/index.html";
        if (ifNoneMatch is not null) context.Request.Headers["If-None-Match"] = ifNoneMatch;
        if (ifModifiedSince is not null) context.Request.Headers["If-Modified-Since"] = ifModifiedSince;

        var sink = new MemoryStream();
        context.Response.Body = sink;

        var middleware = new SidebarInjectionMiddleware(
            async ctx =>
            {
                var conditional = ctx.Request.Headers.ContainsKey("If-None-Match")
                    || ctx.Request.Headers.ContainsKey("If-Modified-Since");
                ctx.Response.Headers["ETag"] = "\"on-disk\"";
                ctx.Response.Headers["Last-Modified"] = "Wed, 24 Sep 2026 00:00:00 GMT";
                if (conditional)
                {
                    ctx.Response.StatusCode = StatusCodes.Status304NotModified;
                    return;
                }

                ctx.Response.ContentType = "text/html";
                await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(Shell));
            },
            NullLogger<SidebarInjectionMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        context.Items["body"] = Encoding.UTF8.GetString(sink.ToArray());
        return context;
    }

    private static string Body(HttpContext context) => (string)context.Items["body"]!;

    [Fact]
    public async Task AReturningBrowserStillReceivesTheBootstrap()
    {
        // Cached before the plugin was installed, so its copy has no bootstrap
        // and its validator still matches the file on disk. Answering 304 here
        // is what left the page working only in a private window.
        var context = await RunAsync(ifNoneMatch: "\"on-disk\"");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("achievementbadges-bootstrap", Body(context), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADateValidatorIsDroppedToo()
    {
        var context = await RunAsync(ifNoneMatch: null, ifModifiedSince: "Wed, 24 Sep 2026 00:00:00 GMT");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("achievementbadges-bootstrap", Body(context), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheInjectedPageCarriesNoValidatorOfTheFileOnDisk()
    {
        // They describe the unpatched file. Kept, they let the browser or a
        // proxy revalidate its way back to a page with no bootstrap.
        var context = await RunAsync(ifNoneMatch: null);

        Assert.Contains("achievementbadges-bootstrap", Body(context), StringComparison.Ordinal);
        Assert.False(context.Response.Headers.ContainsKey("ETag"));
        Assert.False(context.Response.Headers.ContainsKey("Last-Modified"));
    }

    [Fact]
    public async Task AnInstallWhoseDiskPatchTookKeepsItsConditionalRequests()
    {
        // There the file itself carries the bootstrap, so its validators
        // describe a patched body and 304 is the right answer.
        SetDiskPatched(true);

        var context = await RunAsync(ifNoneMatch: "\"on-disk\"");

        Assert.Equal(StatusCodes.Status304NotModified, context.Response.StatusCode);
        Assert.Equal(string.Empty, Body(context));
    }

    [Fact]
    public void EachPackageStampsTheVersionItIsShippedAs()
    {
        // [issue #140] Releases ship X.Y.Z.0 for Jellyfin 10.11 and X.Y.Z.1
        // for Jellyfin 12 from one source tree. Jellyfin shows the assembly's
        // version in the dashboard but resolves uninstall and the plugin image
        // through the manifest's, so the two have to agree: with both
        // assemblies stamped .0 the Jellyfin 12 package could not be
        // uninstalled at all.
        var version = typeof(Plugin).Assembly.GetName().Version!;
        var expected = Environment.Version.Major >= 10 ? 1 : 0;

        Assert.Equal(expected, version.Revision);
    }

    [Fact]
    public void TheReleaseNamesTheZipAfterThatSameComponent()
    {
        // The stamp above is only right while the workflow keeps naming the
        // Jellyfin 12 zip with the same fourth component.
        var workflow = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "release.yml"));

        Assert.Contains("ZIPVER12=\"${ZIPVER%.*}.1\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--framework \"$TFM\"", workflow, StringComparison.Ordinal);
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
