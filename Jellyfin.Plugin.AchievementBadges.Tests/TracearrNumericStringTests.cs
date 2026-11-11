using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.AchievementBadges.Models;
using Jellyfin.Plugin.AchievementBadges.Configuration;
using Jellyfin.Plugin.AchievementBadges.Helpers;
using Jellyfin.Plugin.AchievementBadges.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AchievementBadges.Tests;

// The scan needs Plugin.Instance configured. Other tests must not observe it.
[CollectionDefinition("Tracearr numeric strings", DisableParallelization = true)]
public class TracearrNumericStringCollection;

[Collection("Tracearr numeric strings")]
public class TracearrNumericStringTests
{
    [Theory]
    [InlineData("5400000", 5400000L)]
    [InlineData("\"5400000\"", 5400000L)]
    [InlineData("\"5400000.000\"", 5400000L)]
    [InlineData("\" 5400000.000 \"", 5400000L)]
    [InlineData("\"+5400000\"", 5400000L)]
    [InlineData("\"-5400000.000\"", -5400000L)]
    [InlineData("\"0\"", 0L)]
    [InlineData("\"9223372036854775807.000\"", long.MaxValue)]
    [InlineData("\"-9223372036854775808.000\"", long.MinValue)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    [InlineData("null", null)]
    [InlineData("\"\"", null)]
    [InlineData("\"   \"", null)]
    [InlineData("\"invalid\"", null)]
    [InlineData("\"5400000.001\"", null)]
    [InlineData("\"5400000.00000000000000000000000000001\"", null)]
    [InlineData("\"9223372036854775808\"", null)]
    [InlineData("\"-9223372036854775809\"", null)]
    [InlineData("9223372036854775808", null)]
    [InlineData("5400000.0", null)]
    [InlineData("\"5,400,000\"", null)]
    [InlineData("\"5400000,000\"", null)]
    [InlineData("\"5.4e6\"", null)]
    [InlineData("\"5400000e0\"", null)]
    [InlineData("\"5400000.\"", null)]
    [InlineData("\".000\"", null)]
    [InlineData("\"54 00000\"", null)]
    [InlineData("true", null)]
    [InlineData("{}", null)]
    [InlineData("[]", null)]
    public void DurationMapping_AcceptsOnlyExactDecimalIntegers(string json, long? expected)
    {
        using var culture = new InvariantParsingCulture();
        using var document = JsonDocument.Parse("{\"rating_key\":\"item\",\"duration_ms\":" + json
            + ",\"total_duration_ms\":" + json + "}");
        var play = TracearrClient.MapPlay(document.RootElement);
        Assert.NotNull(play);
        Assert.Equal(expected, play.DurationMs);
        Assert.Equal(expected, play.TotalDurationMs);
    }

    [Theory]
    [InlineData("2024", 2024)]
    [InlineData("\"2024\"", 2024)]
    [InlineData("\" 2024.000 \"", 2024)]
    [InlineData("\"+2024\"", 2024)]
    [InlineData("\"-2024.000\"", -2024)]
    [InlineData("\"2147483647.000\"", int.MaxValue)]
    [InlineData("\"-2147483648.000\"", int.MinValue)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    [InlineData("\"2147483648\"", null)]
    [InlineData("\"-2147483649\"", null)]
    [InlineData("2147483648", null)]
    [InlineData("\"9223372036854775808\"", null)]
    [InlineData("\"2024.1\"", null)]
    [InlineData("\"2024.00000000000000000000000000001\"", null)]
    [InlineData("\"2,024\"", null)]
    [InlineData("\"2024,000\"", null)]
    [InlineData("\"2024e0\"", null)]
    [InlineData("\"2024.\"", null)]
    [InlineData("\".000\"", null)]
    [InlineData("\"\"", null)]
    [InlineData("\"invalid\"", null)]
    [InlineData("null", null)]
    [InlineData("2024.0", null)]
    [InlineData("false", null)]
    [InlineData("{}", null)]
    [InlineData("[]", null)]
    public void YearMapping_UsesTheSameRulesWithinInt32(string json, int? expected)
    {
        using var culture = new InvariantParsingCulture();
        using var document = JsonDocument.Parse("{\"rating_key\":\"item\",\"year\":" + json + "}");
        Assert.Equal(expected, TracearrClient.MapPlay(document.RootElement)!.Year);
    }

    [Fact]
    public void MissingNumericFields_RemainNull()
    {
        using var document = JsonDocument.Parse("""{"rating_key":"item"}""");
        var play = TracearrClient.MapPlay(document.RootElement)!;
        Assert.Null(play.DurationMs);
        Assert.Null(play.TotalDurationMs);
        Assert.Null(play.Year);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HistoryScan_RecoversAlreadyImportedMinutes_AndPreservesCounterFloors(bool skipDailyBuckets)
    {
        const string userId = "11111111-1111-1111-1111-111111111111";
        const string history = """
            {"data":[
              {"id":"play-long","rating_key":"deleted-movie","media_type":"movie",
               "media_title":"Example movie","show_title":null,"year":2024,
               "started_at":"2026-01-01T12:00:00Z","duration_ms":5400000,
               "total_duration_ms":"5400000","watched":true},
              {"id":"play-short","rating_key":"deleted-episode","media_type":"episode",
               "media_title":"Example episode","show_title":"Example series","year":2024,
               "started_at":"2026-01-02T12:00:00Z","duration_ms":1200000,
               "total_duration_ms":"1200000.000","watched":true}
            ],"meta":{"nextCursor":null,"pageSize":100}}
            """;
        var dataDir = Path.Combine(Path.GetTempPath(), "ab-tracearr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var server = ServeHistoryAsync(listener, history, userId, cancellation.Token);
        var instance = typeof(Plugin).GetProperty(nameof(Plugin.Instance), BindingFlags.Public | BindingFlags.Static)!;
        var previousPlugin = Plugin.Instance;
        try
        {
            var paths = new Mock<IApplicationPaths>();
            paths.SetupGet(p => p.PluginConfigurationsPath).Returns(dataDir);
            paths.SetupGet(p => p.PluginsPath).Returns(dataDir);
            var serializer = new Mock<IXmlSerializer>();
            serializer.Setup(s => s.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>()))
                .Returns(new PluginConfiguration());
            var plugin = new Plugin(paths.Object, serializer.Object);
            plugin.Configuration.TracearrUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            plugin.Configuration.TracearrApiToken = "test-only-token";
            plugin.Configuration.BackfillSkipTimeWindowedBadges = skipDailyBuckets;
            var users = new Mock<IUserManager>();
            users.Setup(u => u.GetUserById(Guid.Parse(userId))).Returns(new User("Example", "provider", "reset"));
            var library = new Mock<ILibraryManager>();
            library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
                .Returns(new QueryResult<BaseItem>(Array.Empty<BaseItem>()));
            var ledger = new TracearrCreditLedger();
            using var badges = new AchievementBadgeService(paths.Object, users.Object,
                new WebhookNotifier(NullLogger<WebhookNotifier>.Instance),
                new AuditLogService(paths.Object, NullLogger<AuditLogService>.Instance),
                NullLogger<AchievementBadgeService>.Instance, tracearrLedger: ledger);
            badges.RecordPlayback(userId, isMovie: true, playedAt: DateTimeOffset.Parse("2026-01-01T12:00:00Z", CultureInfo.InvariantCulture));
            var before = badges.PeekProfile(userId)!;
            before.Counters.MoviesWatched = 7;
            before.Counters.RewatchCount = 4;
            before.ScoreBank = 500;
            var unlocked = before.Badges.Where(b => b.Unlocked).Select(b => b.Id).ToArray();
            ledger.Remember(userId, new[] { "play-long", "play-short" });
            using var targets = new TargetProgressService(library.Object, Mock.Of<IUserDataManager>(), users.Object,
                new CustomBadgeService(paths.Object, NullLogger<CustomBadgeService>.Instance), badges,
                NullLogger<TargetProgressService>.Instance);
            var scan = new WatchHistoryBackfillService(library.Object, users.Object, Mock.Of<IUserDataManager>(),
                badges, new LibraryCompletionService(library.Object, users.Object, badges,
                    NullLogger<LibraryCompletionService>.Instance), targets, ledger,
                new WatchCarryStore(TimeSpan.Zero), NullLogger<WatchHistoryBackfillService>.Instance);

            using var sync = JsonSerializer.SerializeToDocument(scan.SyncTracearrForUser(userId));
            Assert.True(sync.RootElement.GetProperty("Success").GetBoolean());
            Assert.Equal(0, sync.RootElement.GetProperty("PlaysCredited").GetInt32());
            Assert.Equal(0, badges.PeekProfile(userId)!.Counters.TotalMinutesWatched);

            for (var pass = 0; pass < 2; pass++)
            {
                using var result = JsonSerializer.SerializeToDocument(scan.BackfillUser(userId));
                Assert.True(result.RootElement.GetProperty("Success").GetBoolean());
                var profile = badges.PeekProfile(userId)!;
                Assert.Equal(110, profile.Counters.TotalMinutesWatched);
                Assert.Equal(90, profile.Counters.LongestItemMinutes);
                Assert.Equal(1, profile.Counters.ShortItemsWatched);
                Assert.Equal(90, profile.Counters.MinutesByDate["2026-01-01"]);
                Assert.Equal(20, profile.Counters.MinutesByDate["2026-01-02"]);
                Assert.Equal(1, profile.Counters.MoviesByDate["2026-01-01"]);
                Assert.Equal(1, profile.Counters.EpisodesByDate["2026-01-02"]);
                Assert.Equal(7, profile.Counters.MoviesWatched);
                Assert.Equal(4, profile.Counters.RewatchCount);
                Assert.True(profile.ScoreBank >= 500);
                Assert.All(unlocked, id => Assert.Contains(profile.Badges, b => b.Id == id && b.Unlocked));
                Assert.Equal(2, ledger.For(userId).Count);
            }
        }
        finally
        {
            instance.SetValue(null, previousPlugin);
            cancellation.Cancel();
            await server;
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(BaseItemKind.Movie, true, true)]
    [InlineData(BaseItemKind.Movie, false, true)]
    [InlineData(BaseItemKind.Movie, true, false)]
    [InlineData(BaseItemKind.Movie, false, false)]
    [InlineData(BaseItemKind.Episode, true, true)]
    [InlineData(BaseItemKind.Episode, false, true)]
    [InlineData(BaseItemKind.Episode, true, false)]
    [InlineData(BaseItemKind.Episode, false, false)]
    [InlineData(BaseItemKind.Book, true, true)]
    [InlineData(BaseItemKind.Book, false, true)]
    [InlineData(BaseItemKind.Book, true, false)]
    [InlineData(BaseItemKind.Book, false, false)]
    [InlineData(BaseItemKind.Audio, true, true)]
    [InlineData(BaseItemKind.Audio, false, true)]
    [InlineData(BaseItemKind.Audio, true, false)]
    [InlineData(BaseItemKind.Audio, false, false)]
    public void LibraryScan_UsesRealDates_AndSkipsFallbackDates(BaseItemKind kind, bool hasDate, bool skipUnreliable)
    {
        using var fixture = new ScanFixture(skipUnreliable);
        BaseItem CreateItem() => kind switch
        {
            BaseItemKind.Movie => new MediaBrowser.Controller.Entities.Movies.Movie(),
            BaseItemKind.Episode => new MediaBrowser.Controller.Entities.TV.Episode(),
            BaseItemKind.Book => new Book(),
            _ => new MediaBrowser.Controller.Entities.Audio.Audio()
        };
        var items = new[] { CreateItem(), CreateItem() };
        foreach (var item in items)
        {
            item.Id = Guid.NewGuid();
            item.RunTimeTicks = 90 * TimeSpan.TicksPerMinute;
        }
        fixture.Library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) => new QueryResult<BaseItem>(
                q.IsPlayed == true && q.IncludeItemTypes.Contains(kind) ? items : Array.Empty<BaseItem>()));
        fixture.UserData.Setup(d => d.GetUserData(It.IsAny<User>(), It.IsAny<BaseItem>()))
            .Returns(new UserItemData { Key = "test-item", LastPlayedDate = hasDate ? new DateTime(2021, 12, 25, 23, 0, 0, DateTimeKind.Utc) : null });

        using var result = JsonSerializer.SerializeToDocument(fixture.Scan.BackfillUser(ScanFixture.UserId));
        Assert.True(result.RootElement.GetProperty("Success").GetBoolean());
        var counters = fixture.Badges.PeekProfile(ScanFixture.UserId)!.Counters;
        var recordDates = hasDate || !skipUnreliable;
        Assert.Equal(recordDates ? 1 : 0, counters.WatchDates.Count);
        Assert.Equal(recordDates ? 1 : 0, counters.DayOfWeekItemCounts.Count);
        Assert.Equal(recordDates && kind == BaseItemKind.Movie ? 1 : 0, counters.MoviesByDate.Count);
        Assert.Equal(recordDates && kind == BaseItemKind.Episode ? 1 : 0, counters.EpisodesByDate.Count);
        Assert.Equal(recordDates && kind != BaseItemKind.Book ? 1 : 0, counters.MinutesByDate.Count);
        if (hasDate)
        {
            Assert.Contains("2021-12-25", counters.WatchDates);
            Assert.Equal(2, counters.DayOfWeekItemCounts["Saturday"]);
            Assert.True(counters.WatchedOnChristmas);
            Assert.Equal(2, counters.LateNightSessions);
        }
        if (skipUnreliable)
        {
            var timeWindowed = AchievementDefinitions.All.Where(d => d.TimeWindow.HasValue).Select(d => d.Id).ToHashSet();
            Assert.DoesNotContain(fixture.Badges.PeekProfile(ScanFixture.UserId)!.Badges,
                b => b.Unlocked && timeWindowed.Contains(b.Id));
            if (hasDate && kind == BaseItemKind.Movie)
            {
                fixture.Badges.RecordPlayback(ScanFixture.UserId, isMovie: true,
                    playedAt: new DateTimeOffset(2021, 12, 25, 23, 0, 0, TimeSpan.Zero));
                Assert.Contains(fixture.Badges.PeekProfile(ScanFixture.UserId)!.Badges,
                    b => b.Id == "double-feature" && b.Unlocked && b.EarnSource == EarnSource.RealTime);
            }
        }
    }

    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    public void UntrustedDates_DoNotInventCalendarProgress(bool skipUnreliable, bool silent, int expected)
    {
        using var fixture = new ScanFixture(skipUnreliable);
        fixture.Badges.RecordPlayback(new PlaybackContext
        {
            UserId = ScanFixture.UserId, IsMovie = true, Silent = silent,
            PlayedAt = new DateTimeOffset(2021, 12, 25, 23, 0, 0, TimeSpan.Zero),
            RunTimeTicks = TimeSpan.TicksPerMinute
        });
        var counters = fixture.Badges.PeekProfile(ScanFixture.UserId)!.Counters;
        Assert.Equal(expected, counters.WatchDates.Count);
        Assert.Equal(expected, counters.DayOfWeekItemCounts.Count);
        Assert.Equal(expected, counters.BestWatchStreak);
        Assert.Equal(expected == 1, counters.LastWatchDate.HasValue);
        Assert.Equal(expected == 1, counters.WatchedOnChristmas);
        Assert.Equal(expected, counters.LateNightSessions);
        Assert.Equal(expected, counters.WeekendSessions);
        Assert.Equal(1, counters.TotalItemsWatched);
        Assert.Equal(1, counters.TotalMinutesWatched);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanScope_IsLimitedToItsUser_AndRestoredEvenOnFailure(bool failScan)
    {
        using var fixture = new ScanFixture(true);
        const string other = "22222222-2222-2222-2222-222222222222";
        var invoked = false;
        fixture.Library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback(() =>
            {
                if (invoked) return;
                invoked = true;
                fixture.Badges.RecordPlayback(other, isMovie: true);
                fixture.Badges.RecordPlayback(other, isMovie: true);
                if (failScan) throw new InvalidOperationException("Test scan failure");
            })
            .Returns(new QueryResult<BaseItem>(Array.Empty<BaseItem>()));
        using var result = JsonSerializer.SerializeToDocument(fixture.Scan.BackfillUser(ScanFixture.UserId));
        Assert.Equal(!failScan, result.RootElement.GetProperty("Success").GetBoolean());
        Assert.Contains(fixture.Badges.PeekProfile(other)!.Badges,
            b => b.Id == "double-feature" && b.Unlocked && b.EarnSource == EarnSource.RealTime);
        fixture.Badges.RecordPlayback(ScanFixture.UserId, isMovie: true);
        fixture.Badges.RecordPlayback(ScanFixture.UserId, isMovie: true);
        Assert.Contains(fixture.Badges.PeekProfile(ScanFixture.UserId)!.Badges,
            b => b.Id == "double-feature" && b.Unlocked && b.EarnSource == EarnSource.RealTime);
    }

    private sealed class ScanFixture : IDisposable
    {
        public const string UserId = "11111111-1111-1111-1111-111111111111";
        private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "ab-dates-" + Guid.NewGuid().ToString("N"));
        private readonly Plugin? _previous = Plugin.Instance;
        public Mock<ILibraryManager> Library { get; } = new();
        public Mock<IUserDataManager> UserData { get; } = new();
        public AchievementBadgeService Badges { get; }
        public WatchHistoryBackfillService Scan { get; }
        private readonly TargetProgressService _targets;
        public ScanFixture(bool skipUnreliable)
        {
            Directory.CreateDirectory(_dataDir);
            var paths = new Mock<IApplicationPaths>();
            paths.SetupGet(p => p.PluginConfigurationsPath).Returns(_dataDir);
            paths.SetupGet(p => p.PluginsPath).Returns(_dataDir);
            var serializer = new Mock<IXmlSerializer>();
            serializer.Setup(s => s.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>()))
                .Returns(new PluginConfiguration { BackfillSkipTimeWindowedBadges = skipUnreliable });
            _ = new Plugin(paths.Object, serializer.Object);
            var users = new Mock<IUserManager>();
            users.Setup(u => u.GetUserById(Guid.Parse(UserId))).Returns(new User("Example", "provider", "reset"));
            Library.Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
                .Returns(new QueryResult<BaseItem>(Array.Empty<BaseItem>()));
            var ledger = new TracearrCreditLedger();
            Badges = new AchievementBadgeService(paths.Object, users.Object,
                new WebhookNotifier(NullLogger<WebhookNotifier>.Instance),
                new AuditLogService(paths.Object, NullLogger<AuditLogService>.Instance),
                NullLogger<AchievementBadgeService>.Instance, tracearrLedger: ledger);
            _targets = new TargetProgressService(Library.Object, UserData.Object, users.Object,
                new CustomBadgeService(paths.Object, NullLogger<CustomBadgeService>.Instance), Badges,
                NullLogger<TargetProgressService>.Instance);
            Scan = new WatchHistoryBackfillService(Library.Object, users.Object, UserData.Object, Badges,
                new LibraryCompletionService(Library.Object, users.Object, Badges, NullLogger<LibraryCompletionService>.Instance),
                _targets, ledger, new WatchCarryStore(TimeSpan.Zero), NullLogger<WatchHistoryBackfillService>.Instance);
        }
        public void Dispose()
        {
            _targets.Dispose();
            Badges.Dispose();
            typeof(Plugin).GetProperty(nameof(Plugin.Instance))!.SetValue(null, _previous);
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private static async Task ServeHistoryAsync(TcpListener listener, string history, string userId, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = await reader.ReadLineAsync(ct);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct))) { }
                var body = request!.Contains("/history?", StringComparison.Ordinal)
                    ? history
                    : "{\"data\":[{\"id\":\"test-account\",\"accounts\":[{\"external_user_id\":\"" + userId + "\"}]}]}";
                var payload = Encoding.UTF8.GetBytes(body);
                var headers = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "
                    + payload.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, ct);
                await stream.WriteAsync(payload, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private sealed class InvariantParsingCulture : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
        public InvariantParsingCulture() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
