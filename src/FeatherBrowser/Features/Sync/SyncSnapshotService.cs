using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Features.Sync.Models;
using FeatherBrowser.Infrastructure.Persistence;

namespace FeatherBrowser.Features.Sync;

internal sealed class SyncSnapshotService
{
    private const int MaxSnapshotBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public SyncSnapshot Capture(BrowserDataStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        BrowserSettings settings = store.Settings;

        return new SyncSnapshot
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Bookmarks = store.Bookmarks
                .Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.Url))
                .Select(CloneBookmark)
                .ToList(),
            Settings = SyncedBrowserSettings.From(settings),
            History = settings.SyncHistory
                ? store.History.Select(CloneHistory).ToList()
                : null,
            Session = null,
            DeviceTabs =
            [
                new DeviceTabs
                {
                    DeviceId = store.InstallationId,
                    DeviceName = Environment.MachineName,
                    Tabs = settings.SyncOpenTabs
                        ? store.LoadSessionState().TabStates.Select(CloneTab).ToList()
                        : []
                }
            ]
        };
    }

    public byte[] Serialize(SyncSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);

        if (data.Length > MaxSnapshotBytes)
        {
            CryptographicOperations.ZeroMemory(data);
            throw new InvalidDataException("The Feather Sync snapshot is too large.");
        }

        return data;
    }

    public SyncSnapshot Clone(SyncSnapshot snapshot)
    {
        byte[] data = Serialize(snapshot);
        try
        {
            return Deserialize(data);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    public SyncSnapshot Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data.Length > MaxSnapshotBytes)
            throw new InvalidDataException("The Feather Sync snapshot size is invalid.");

        SyncSnapshot snapshot = JsonSerializer.Deserialize<SyncSnapshot>(data, JsonOptions)
            ?? throw new InvalidDataException("The Feather Sync snapshot is empty.");

        if (snapshot.FormatVersion != 1)
            throw new InvalidDataException("The Feather Sync snapshot format is not supported.");

        return Normalize(snapshot);
    }

    public void Apply(BrowserDataStore store, SyncSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(snapshot);

        snapshot.Settings.ApplyTo(store.Settings);
        store.Settings.SyncEnabled = true;
        bool saved = store.SaveSettings();
        saved &= store.ReplaceBookmarks(snapshot.Bookmarks);

        if (snapshot.Settings.SyncHistory && snapshot.History is not null)
            saved &= store.ReplaceHistory(snapshot.History);

        string self = store.InstallationId;
        store.OtherDeviceTabs = (snapshot.DeviceTabs ?? [])
            .Where(device => !string.Equals(device.DeviceId, self, StringComparison.Ordinal) && device.Tabs.Count > 0)
            .Select(CloneDeviceTabs)
            .ToList();

        if (!saved)
            throw new IOException("Feather could not save synced data to disk. Check free space and folder permissions, then sync again.");
    }

    public bool HasSameContent(SyncSnapshot left, SyncSnapshot right) => Hash(left) == Hash(right);

    public SyncSnapshot Merge(
        SyncSnapshot local,
        SyncSnapshot remote,
        SyncLocalState? baseline)
    {
        SyncSectionHashes localHashes = Hash(local);
        SyncSectionHashes remoteHashes = Hash(remote);

        if (baseline is null)
        {
            return Normalize(new SyncSnapshot
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Bookmarks = MergeBookmarks(local.Bookmarks, remote.Bookmarks),
                Settings = remote.Settings,
                History = MergeHistory(local.History, remote.History),
                DeviceTabs = MergeDeviceTabs(local.DeviceTabs, remote.DeviceTabs)
            });
        }

        bool localBookmarksChanged = localHashes.Bookmarks != baseline.BookmarksHash;
        bool remoteBookmarksChanged = remoteHashes.Bookmarks != baseline.BookmarksHash;
        bool localSettingsChanged = localHashes.Settings != baseline.SettingsHash;
        bool remoteSettingsChanged = remoteHashes.Settings != baseline.SettingsHash;
        bool localHistoryChanged = localHashes.History != baseline.HistoryHash;
        bool remoteHistoryChanged = remoteHashes.History != baseline.HistoryHash;

        SyncSnapshot? common = baseline.Snapshot;

        List<BrowserBookmark> bookmarks =
            !localBookmarksChanged && remoteBookmarksChanged ? remote.Bookmarks.Select(CloneBookmark).ToList() :
            localBookmarksChanged && !remoteBookmarksChanged ? local.Bookmarks.Select(CloneBookmark).ToList() :
            localBookmarksChanged && remoteBookmarksChanged
                ? common is not null
                    ? MergeBookmarks(local.Bookmarks, remote.Bookmarks, common.Bookmarks)
                    : MergeBookmarks(local.Bookmarks, remote.Bookmarks)
                : local.Bookmarks.Select(CloneBookmark).ToList();

        SyncedBrowserSettings settings =
            !localSettingsChanged && remoteSettingsChanged ? remote.Settings :
            localSettingsChanged && remoteSettingsChanged && common is not null
                ? MergeSettings(local.Settings, remote.Settings, common.Settings)
                : local.Settings;

        List<HistoryEntry>? history =
            !localHistoryChanged && remoteHistoryChanged ? CloneHistoryList(remote.History) :
            localHistoryChanged && !remoteHistoryChanged ? CloneHistoryList(local.History) :
            localHistoryChanged && remoteHistoryChanged
                ? common?.History is not null
                    ? MergeHistory(local.History, remote.History, common.History)
                    : MergeHistory(local.History, remote.History)
                : CloneHistoryList(local.History);

        return Normalize(new SyncSnapshot
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Bookmarks = bookmarks,
            Settings = settings,
            History = settings.SyncHistory ? history : null,
            DeviceTabs = MergeDeviceTabs(local.DeviceTabs, remote.DeviceTabs)
        });
    }

    public SyncSectionHashes Hash(SyncSnapshot snapshot) =>
        new(
            HashObject(snapshot.Bookmarks),
            HashObject(snapshot.Settings),
            HashObject(snapshot.History),
            HashObject(snapshot.DeviceTabs));

    public string HashBytes(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data));

    private static SyncSnapshot Normalize(SyncSnapshot snapshot)
    {
        snapshot.Bookmarks.RemoveAll(bookmark => string.IsNullOrWhiteSpace(bookmark.Url));
        snapshot.Bookmarks = snapshot.Bookmarks
            .GroupBy(bookmark => bookmark.Url, UrlIdentity.Comparer)
            .Select(group => group.OrderByDescending(item => item.CreatedAt).First())
            .OrderByDescending(bookmark => bookmark.CreatedAt)
            .Take(5000)
            .Select(CloneBookmark)
            .ToList();

        if (snapshot.History is not null)
        {
            snapshot.History = snapshot.History
                .Where(entry => Uri.TryCreate(entry.Url, UriKind.Absolute, out Uri? uri) &&
                                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                .GroupBy(entry => entry.Url, UrlIdentity.Comparer)
                .Select(group => group.OrderByDescending(item => item.LastVisited).First())
                .OrderByDescending(entry => entry.LastVisited)
                .Take(3000)
                .Select(CloneHistory)
                .ToList();
        }

        snapshot.DeviceTabs ??= [];
        snapshot.Session = null;
        snapshot.Settings.QuickLinks ??= [];
        snapshot.Settings.KeepAliveSites ??= [];
        snapshot.Settings.AllowlistedSites ??= [];
        snapshot.Settings.CustomBlockRules ??= [];
        snapshot.Settings.Workspaces ??= [];

        return snapshot;
    }

    private static List<BrowserBookmark> MergeBookmarks(
        IEnumerable<BrowserBookmark> local,
        IEnumerable<BrowserBookmark> remote)
    {
        var byUrl = new Dictionary<string, BrowserBookmark>(UrlIdentity.Comparer);

        foreach (BrowserBookmark item in local.Concat(remote).Where(item => !string.IsNullOrWhiteSpace(item.Url)))
        {
            if (!byUrl.TryGetValue(item.Url, out BrowserBookmark? existing) || item.CreatedAt > existing.CreatedAt)
                byUrl[item.Url] = CloneBookmark(item);
        }

        return byUrl.Values
            .OrderByDescending(item => item.CreatedAt)
            .Take(5000)
            .ToList();
    }

    private static List<HistoryEntry>? MergeHistory(
        IEnumerable<HistoryEntry>? local,
        IEnumerable<HistoryEntry>? remote)
    {
        if (local is null && remote is null)
            return null;

        var byUrl = new Dictionary<string, HistoryEntry>(UrlIdentity.Comparer);

        foreach (HistoryEntry item in (local ?? []).Concat(remote ?? []).Where(item => !string.IsNullOrWhiteSpace(item.Url)))
        {
            if (!byUrl.TryGetValue(item.Url, out HistoryEntry? existing))
            {
                byUrl[item.Url] = CloneHistory(item);
                continue;
            }

            if (item.LastVisited > existing.LastVisited)
            {
                existing.LastVisited = item.LastVisited;
                existing.Title = item.Title;
            }

            existing.VisitCount = Math.Max(existing.VisitCount, item.VisitCount);
        }

        return byUrl.Values
            .OrderByDescending(item => item.LastVisited)
            .Take(3000)
            .ToList();
    }

    internal static List<BrowserBookmark> MergeBookmarks(
        IEnumerable<BrowserBookmark> local,
        IEnumerable<BrowserBookmark> remote,
        IEnumerable<BrowserBookmark> common)
    {
        Dictionary<string, BrowserBookmark> localByUrl = IndexByUrl(local, static item => item.Url);
        Dictionary<string, BrowserBookmark> remoteByUrl = IndexByUrl(remote, static item => item.Url);
        Dictionary<string, BrowserBookmark> commonByUrl = IndexByUrl(common, static item => item.Url);
        var merged = new List<BrowserBookmark>();

        foreach (string url in localByUrl.Keys.Union(remoteByUrl.Keys, UrlIdentity.Comparer))
        {
            localByUrl.TryGetValue(url, out BrowserBookmark? mine);
            remoteByUrl.TryGetValue(url, out BrowserBookmark? theirs);

            if (commonByUrl.TryGetValue(url, out BrowserBookmark? before) && (mine is null || theirs is null))
            {
                BrowserBookmark survivor = mine ?? theirs!;
                if (!BookmarkEdited(survivor, before))
                    continue;
                merged.Add(CloneBookmark(survivor));
                continue;
            }

            BrowserBookmark pick = mine is null ? theirs! :
                theirs is null ? mine :
                theirs.CreatedAt > mine.CreatedAt || (BookmarkEdited(theirs, mine) && !BookmarkEdited(mine, before ?? mine))
                    ? theirs
                    : mine;
            merged.Add(CloneBookmark(pick));
        }

        return merged
            .OrderByDescending(item => item.CreatedAt)
            .Take(5000)
            .ToList();
    }

    internal static List<HistoryEntry>? MergeHistory(
        IEnumerable<HistoryEntry>? local,
        IEnumerable<HistoryEntry>? remote,
        IEnumerable<HistoryEntry> common)
    {
        if (local is null && remote is null)
            return null;

        Dictionary<string, HistoryEntry> localByUrl = IndexByUrl(local ?? [], static item => item.Url);
        Dictionary<string, HistoryEntry> remoteByUrl = IndexByUrl(remote ?? [], static item => item.Url);
        Dictionary<string, HistoryEntry> commonByUrl = IndexByUrl(common, static item => item.Url);
        var merged = new List<HistoryEntry>();

        foreach (string url in localByUrl.Keys.Union(remoteByUrl.Keys, UrlIdentity.Comparer))
        {
            localByUrl.TryGetValue(url, out HistoryEntry? mine);
            remoteByUrl.TryGetValue(url, out HistoryEntry? theirs);

            if (commonByUrl.TryGetValue(url, out HistoryEntry? before) && (mine is null || theirs is null))
            {
                HistoryEntry survivor = mine ?? theirs!;
                if (survivor.LastVisited > before.LastVisited)
                    merged.Add(CloneHistory(survivor));
                continue;
            }

            if (mine is null || theirs is null)
            {
                merged.Add(CloneHistory(mine ?? theirs!));
                continue;
            }

            HistoryEntry newer = theirs.LastVisited > mine.LastVisited ? theirs : mine;
            HistoryEntry entry = CloneHistory(newer);
            entry.VisitCount = Math.Max(mine.VisitCount, theirs.VisitCount);
            merged.Add(entry);
        }

        return merged
            .OrderByDescending(item => item.LastVisited)
            .Take(3000)
            .ToList();
    }

    internal static SyncedBrowserSettings MergeSettings(
        SyncedBrowserSettings local,
        SyncedBrowserSettings remote,
        SyncedBrowserSettings common)
    {
        JsonObject mine = (JsonObject)JsonSerializer.SerializeToNode(local, JsonOptions)!;
        JsonObject theirs = (JsonObject)JsonSerializer.SerializeToNode(remote, JsonOptions)!;
        JsonObject before = (JsonObject)JsonSerializer.SerializeToNode(common, JsonOptions)!;
        var merged = new JsonObject();

        foreach ((string name, JsonNode? value) in mine)
        {
            JsonNode? chosen = JsonNode.DeepEquals(value, before[name]) && theirs.TryGetPropertyValue(name, out JsonNode? other)
                ? other
                : value;
            merged[name] = chosen?.DeepClone();
        }

        return merged.Deserialize<SyncedBrowserSettings>(JsonOptions) ?? local;
    }

    private static bool BookmarkEdited(BrowserBookmark item, BrowserBookmark before) =>
        !string.Equals(item.Title, before.Title, StringComparison.Ordinal) ||
        !string.Equals(item.Folder, before.Folder, StringComparison.Ordinal);

    private static Dictionary<string, T> IndexByUrl<T>(IEnumerable<T> items, Func<T, string?> url)
    {
        var index = new Dictionary<string, T>(UrlIdentity.Comparer);
        foreach (T item in items)
        {
            string? address = url(item);
            if (!string.IsNullOrWhiteSpace(address))
                index.TryAdd(address, item);
        }
        return index;
    }

    private static BrowserBookmark CloneBookmark(BrowserBookmark item) =>
        new()
        {
            Title = item.Title,
            Url = item.Url,
            Folder = item.Folder,
            CreatedAt = item.CreatedAt
        };

    private static HistoryEntry CloneHistory(HistoryEntry item) =>
        new()
        {
            Title = item.Title,
            Url = item.Url,
            LastVisited = item.LastVisited,
            VisitCount = item.VisitCount
        };

    private static List<HistoryEntry>? CloneHistoryList(IEnumerable<HistoryEntry>? items) =>
        items?.Select(CloneHistory).ToList();

    internal static List<DeviceTabs> MergeDeviceTabs(List<DeviceTabs>? local, List<DeviceTabs>? remote)
    {
        var byDevice = new Dictionary<string, DeviceTabs>(StringComparer.Ordinal);
        foreach (DeviceTabs device in (remote ?? []).Concat(local ?? []).Where(device => !string.IsNullOrWhiteSpace(device.DeviceId)))
            byDevice[device.DeviceId] = CloneDeviceTabs(device);

        return byDevice.Values
            .Where(device => device.Tabs.Count > 0)
            .OrderBy(device => device.DeviceId, StringComparer.Ordinal)
            .Take(10)
            .ToList();
    }

    private static DeviceTabs CloneDeviceTabs(DeviceTabs device) =>
        new()
        {
            DeviceId = device.DeviceId,
            DeviceName = device.DeviceName,
            Tabs = device.Tabs.Take(40).Select(CloneTab).ToList()
        };

    private static SessionTabState CloneTab(SessionTabState tab) =>
        new()
        {
            Address = tab.Address,
            Title = tab.Title,
            Workspace = tab.Workspace,
            IsPinned = tab.IsPinned
        };

    private static SessionState CloneSession(SessionState state) =>
        new()
        {
            Tabs = [.. state.Tabs],
            TabStates = state.TabStates.Select(tab => new SessionTabState
            {
                Address = tab.Address,
                Title = tab.Title,
                Workspace = tab.Workspace,
                IsPinned = tab.IsPinned
            }).ToList(),
            ActiveIndex = state.ActiveIndex,
            SavedAt = state.SavedAt
        };

    private static SessionState? CloneNullableSession(SessionState? state) =>
        state is null ? null : CloneSession(state);

    private static string HashObject<T>(T value)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try
        {
            return Convert.ToHexString(SHA256.HashData(data));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }
}

internal sealed record SyncSectionHashes(
    string Bookmarks,
    string Settings,
    string History,
    string Session);
