using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
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
            Session = settings.SyncOpenTabs
                ? CloneSession(store.LoadSessionState())
                : null
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
        store.SaveSettings();
        store.ReplaceBookmarks(snapshot.Bookmarks);

        if (snapshot.Settings.SyncHistory && snapshot.History is not null)
            store.ReplaceHistory(snapshot.History);

        if (snapshot.Settings.SyncOpenTabs && snapshot.Session is not null)
            store.SaveSessionState(snapshot.Session.TabStates, snapshot.Session.ActiveIndex);
    }

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
                Session = remote.Session ?? local.Session
            });
        }

        bool localBookmarksChanged = localHashes.Bookmarks != baseline.BookmarksHash;
        bool remoteBookmarksChanged = remoteHashes.Bookmarks != baseline.BookmarksHash;
        bool localSettingsChanged = localHashes.Settings != baseline.SettingsHash;
        bool remoteSettingsChanged = remoteHashes.Settings != baseline.SettingsHash;
        bool localHistoryChanged = localHashes.History != baseline.HistoryHash;
        bool remoteHistoryChanged = remoteHashes.History != baseline.HistoryHash;
        bool localSessionChanged = localHashes.Session != baseline.SessionHash;
        bool remoteSessionChanged = remoteHashes.Session != baseline.SessionHash;

        List<BrowserBookmark> bookmarks =
            !localBookmarksChanged && remoteBookmarksChanged ? remote.Bookmarks.Select(CloneBookmark).ToList() :
            localBookmarksChanged && !remoteBookmarksChanged ? local.Bookmarks.Select(CloneBookmark).ToList() :
            localBookmarksChanged && remoteBookmarksChanged ? MergeBookmarks(local.Bookmarks, remote.Bookmarks) :
            local.Bookmarks.Select(CloneBookmark).ToList();

        SyncedBrowserSettings settings =
            !localSettingsChanged && remoteSettingsChanged ? remote.Settings : local.Settings;

        List<HistoryEntry>? history =
            !localHistoryChanged && remoteHistoryChanged ? CloneHistoryList(remote.History) :
            localHistoryChanged && !remoteHistoryChanged ? CloneHistoryList(local.History) :
            localHistoryChanged && remoteHistoryChanged ? MergeHistory(local.History, remote.History) :
            CloneHistoryList(local.History);

        SessionState? session =
            !localSessionChanged && remoteSessionChanged ? CloneNullableSession(remote.Session) :
            CloneNullableSession(local.Session);

        return Normalize(new SyncSnapshot
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Bookmarks = bookmarks,
            Settings = settings,
            History = settings.SyncHistory ? history : null,
            Session = settings.SyncOpenTabs ? session : null
        });
    }

    public SyncSectionHashes Hash(SyncSnapshot snapshot) =>
        new(
            HashObject(snapshot.Bookmarks),
            HashObject(snapshot.Settings),
            HashObject(snapshot.History),
            HashObject(snapshot.Session));

    public string HashBytes(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data));

    private static SyncSnapshot Normalize(SyncSnapshot snapshot)
    {
        snapshot.Bookmarks.RemoveAll(bookmark => string.IsNullOrWhiteSpace(bookmark.Url));
        snapshot.Bookmarks = snapshot.Bookmarks
            .GroupBy(bookmark => bookmark.Url, StringComparer.OrdinalIgnoreCase)
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
                .GroupBy(entry => entry.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.LastVisited).First())
                .OrderByDescending(entry => entry.LastVisited)
                .Take(3000)
                .Select(CloneHistory)
                .ToList();
        }

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
        var byUrl = new Dictionary<string, BrowserBookmark>(StringComparer.OrdinalIgnoreCase);

        foreach (BrowserBookmark item in local.Concat(remote))
        {
            if (string.IsNullOrWhiteSpace(item.Url))
                continue;

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

        var byUrl = new Dictionary<string, HistoryEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (HistoryEntry item in (local ?? []).Concat(remote ?? []))
        {
            if (string.IsNullOrWhiteSpace(item.Url))
                continue;

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
