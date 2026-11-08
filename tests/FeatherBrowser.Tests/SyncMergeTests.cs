using FeatherBrowser.Domain.Models;
using FeatherBrowser.Features.Sync;
using FeatherBrowser.Features.Sync.Models;

namespace FeatherBrowser.Tests;

public class SyncMergeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly SyncSnapshotService _service = new();

    private static BrowserBookmark Bookmark(string url, string title = "Page", int minutes = 0) =>
        new() { Url = url, Title = title, CreatedAt = T0.AddMinutes(minutes) };

    private static HistoryEntry Visit(string url, int minutes) =>
        new() { Url = url, Title = url, LastVisited = T0.AddMinutes(minutes), VisitCount = 1 };

    private SyncSnapshot Snapshot(IEnumerable<BrowserBookmark>? bookmarks = null, SyncedBrowserSettings? settings = null,
        IEnumerable<HistoryEntry>? history = null)
    {
        settings ??= new SyncedBrowserSettings { SearchEngine = "Google" };
        if (history is not null)
            settings.SyncHistory = true;

        return _service.Clone(new SyncSnapshot
        {
            Bookmarks = bookmarks?.ToList() ?? [],
            Settings = settings,
            History = history?.ToList()
        });
    }

    private SyncLocalState Baseline(SyncSnapshot common)
    {
        SyncSectionHashes hashes = _service.Hash(common);
        return new SyncLocalState
        {
            UserId = Guid.NewGuid().ToString(),
            BookmarksHash = hashes.Bookmarks,
            SettingsHash = hashes.Settings,
            HistoryHash = hashes.History,
            SessionHash = hashes.Session,
            Snapshot = common
        };
    }

    [Fact]
    public void BookmarkDeletedOnOneDeviceStaysDeletedWhenTheOtherAddsOne()
    {
        SyncSnapshot common = Snapshot([Bookmark("https://x.example/")]);
        SyncSnapshot local = Snapshot([]);                                                   // A deleted X
        SyncSnapshot remote = Snapshot([Bookmark("https://x.example/"), Bookmark("https://y.example/", minutes: 5)]); // B added Y

        SyncSnapshot merged = _service.Merge(local, remote, Baseline(common));

        Assert.Equal(["https://y.example/"], merged.Bookmarks.Select(b => b.Url));
    }

    [Fact]
    public void ClearedHistoryKeepsOnlyVisitsMadeAfterTheLastSync()
    {
        SyncSnapshot common = Snapshot(history: [Visit("https://old.example/", 0)]);
        SyncSnapshot local = Snapshot(history: [Visit("https://local-new.example/", 10)]);   // cleared, then browsed
        SyncSnapshot remote = Snapshot(history: [Visit("https://old.example/", 0), Visit("https://remote-new.example/", 8)]);

        SyncSnapshot merged = _service.Merge(local, remote, Baseline(common));

        Assert.Equal(
            ["https://local-new.example/", "https://remote-new.example/"],
            merged.History!.Select(h => h.Url));
    }

    [Fact]
    public void IndependentSettingsEditsOnTwoDevicesBothSurvive()
    {
        SyncSnapshot common = Snapshot(settings: new SyncedBrowserSettings { SearchEngine = "Google", CompactUi = false });
        SyncSnapshot local = Snapshot(settings: new SyncedBrowserSettings { SearchEngine = "Google", CompactUi = true });
        SyncSnapshot remote = Snapshot(settings: new SyncedBrowserSettings { SearchEngine = "Bing", CompactUi = false });

        SyncSnapshot merged = _service.Merge(local, remote, Baseline(common));

        Assert.Equal("Bing", merged.Settings.SearchEngine);
        Assert.True(merged.Settings.CompactUi);
    }

    [Fact]
    public void UnchangedMergeHasSameContentDespiteNewTimestamp()
    {
        SyncSnapshot common = Snapshot([Bookmark("https://x.example/")]);
        SyncSnapshot merged = _service.Merge(Snapshot([Bookmark("https://x.example/")]), common, Baseline(common));

        Assert.NotEqual(common.GeneratedAt, merged.GeneratedAt);
        Assert.True(_service.HasSameContent(merged, common));
    }

    [Fact]
    public void AddressesThatDifferOnlyInPathCaseStayDistinct()
    {
        SyncSnapshot merged = _service.Merge(
            Snapshot([Bookmark("https://Example.com/File")]),
            Snapshot([Bookmark("https://example.com/file"), Bookmark("HTTPS://EXAMPLE.COM/File", minutes: 1)]),
            baseline: null);

        Assert.Equal(2, merged.Bookmarks.Count);
    }
}

public class SyncedTabsTests
{
    private static DeviceTabs Device(string id, params string[] urls) =>
        new() { DeviceId = id, DeviceName = id, Tabs = urls.Select(u => new SessionTabState { Address = u }).ToList() };

    [Fact]
    public void EachDeviceReplacesOnlyItsOwnTabs()
    {
        List<DeviceTabs> merged = SyncSnapshotService.MergeDeviceTabs(
            local: [Device("laptop", "https://new.example/")],
            remote: [Device("laptop", "https://old.example/"), Device("desktop", "https://desk.example/")]);

        Assert.Equal(["desktop", "laptop"], merged.Select(d => d.DeviceId));
        Assert.Equal("https://new.example/", merged.Single(d => d.DeviceId == "laptop").Tabs.Single().Address);
        Assert.Equal("https://desk.example/", merged.Single(d => d.DeviceId == "desktop").Tabs.Single().Address);
    }

    [Fact]
    public void TurningOffTabSyncRemovesThisDevicesEntry()
    {
        List<DeviceTabs> merged = SyncSnapshotService.MergeDeviceTabs(
            local: [Device("laptop")],
            remote: [Device("laptop", "https://old.example/"), Device("desktop", "https://desk.example/")]);

        Assert.Equal(["desktop"], merged.Select(d => d.DeviceId));
    }
}

public class UrlIdentityTests
{
    [Theory]
    [InlineData("https://Example.com/a", "https://example.com/a", true)]
    [InlineData("HTTPS://example.com:443/a", "https://example.com/a", true)]
    [InlineData("https://example.com/File", "https://example.com/file", false)]
    [InlineData("https://example.com/?q=A", "https://example.com/?q=a", false)]
    public void ComparesHostCaseInsensitivelyAndPathCaseSensitively(string left, string right, bool same)
    {
        Assert.Equal(same, UrlIdentity.Equals(left, right));
    }
}
