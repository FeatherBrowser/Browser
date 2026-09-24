using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using FeatherBrowser.Domain.Models;

namespace FeatherBrowser.Infrastructure.Persistence;

internal sealed class BrowserDataStore
{
    private const string ApplicationDirectoryName = "FeatherBrowser";
    private const string DataDirectoryName = "Data";

    private const string BackupSuffix = ".bak";
    private const string TemporarySuffix = ".tmp";

    private const string DefaultBookmarkFolder = "Favorites";
    private const string DownloadInProgressState = "In progress";

    private const int MaxFavicons = 256;
    private const int MaxFaviconBytes = 65_536;
    private const int MaxHistoryEntries = 3_000;
    private const int MaxDownloadEntries = 500;
    private const int MaxSessionTabs = 40;
    private const int MaxWorkspaces = 12;

    private const int SchemaVersion4 = 4;
    private const int SchemaVersion5 = 5;
    private const int SchemaVersion6 = 6;

    private const string FaviconDataPrefix = "data:image/png;base64,";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _sync = new();

    private readonly string _root;
    private readonly string _bookmarksPath;
    private readonly string _historyPath;
    private readonly string _settingsPath;
    private readonly string _sessionPath;
    private readonly string _downloadsPath;
    private readonly string _faviconsPath;

    private readonly bool _hadExistingSettings;
    private readonly Dictionary<string, string> _favicons;

    public List<BrowserBookmark> Bookmarks { get; private set; }
    public List<HistoryEntry> History { get; private set; }
    public BrowserSettings Settings { get; private set; }
    public List<DownloadEntry> Downloads { get; private set; }

    public BrowserDataStore()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName,
            DataDirectoryName);

        Directory.CreateDirectory(_root);

        _bookmarksPath = GetDataPath("bookmarks.json");
        _historyPath = GetDataPath("history.json");
        _settingsPath = GetDataPath("settings.json");
        _sessionPath = GetDataPath("session.json");
        _downloadsPath = GetDataPath("downloads.json");
        _faviconsPath = GetDataPath("favicons.json");

        _hadExistingSettings = File.Exists(_settingsPath);

        Bookmarks = Load(_bookmarksPath, new List<BrowserBookmark>());
        History = Load(_historyPath, new List<HistoryEntry>());
        Settings = Load(_settingsPath, new BrowserSettings());
        Downloads = Load(_downloadsPath, new List<DownloadEntry>());
        _favicons = LoadFavicons();

        MigrateSettings();
    }

    private string GetDataPath(string fileName) => Path.Combine(_root, fileName);

    private void MigrateSettings()
    {
        bool changed = false;

        if (Settings.SettingsSchemaVersion < SchemaVersion4)
        {
            MigrateToSchema4();
            changed = true;
        }

        if (Settings.SettingsSchemaVersion < SchemaVersion5)
        {
            MigrateToSchema5();
            changed = true;
        }

        if (Settings.SettingsSchemaVersion < SchemaVersion6)
        {
            MigrateToSchema6();
            changed = true;
        }

        changed |= NormalizeSettings();

        if (changed)
            Save(_settingsPath, Settings);
    }

    private void MigrateToSchema4()
    {
        Settings.MaxLoadedTabs = 1;
        Settings.UnloadAfterSeconds = Math.Min(
            Settings.UnloadAfterSeconds <= 0 ? 5 : Settings.UnloadAfterSeconds,
            5);
        Settings.SleepAfterSeconds = Math.Min(
            Settings.SleepAfterSeconds <= 0 ? 2 : Settings.SleepAfterSeconds,
            2);

        Settings.LowMemoryMode = true;
        Settings.EcoMode = true;
        Settings.AutoMemoryGuard = true;
        Settings.MemoryGuardMb = 700;
        Settings.HibernateWhenMinimized = true;
        Settings.CrashRecoveryAutosave = true;
        Settings.ShowWorkspaceSidebar = true;
        Settings.AdaptiveMemoryMode = true;
        Settings.ColdUnloadOtherWorkspaces = true;
        Settings.StartupMode = Settings.RestorePreviousSession ? "Restore" : "NewTab";
        Settings.SettingsSchemaVersion = SchemaVersion4;
    }

    private void MigrateToSchema5()
    {
        Settings.SitePermissions ??= new Dictionary<string, string>();
        Settings.BlockNotificationPrompts = true;
        Settings.SendDoNotTrack = true;
        Settings.SettingsSchemaVersion = SchemaVersion5;
    }

    private void MigrateToSchema6()
    {
        Settings.FirstRunCompleted = _hadExistingSettings;
        Settings.DataBackupsEnabled = true;
        Settings.SettingsSchemaVersion = SchemaVersion6;
    }

    private bool NormalizeSettings()
    {
        bool changed = false;

        if (Settings.SitePermissions is null)
        {
            Settings.SitePermissions = new Dictionary<string, string>();
            changed = true;
        }

        if (Settings.KeepAliveSites is null)
        {
            Settings.KeepAliveSites = [];
            changed = true;
        }

        if (Settings.AllowlistedSites is null)
        {
            Settings.AllowlistedSites = [];
            changed = true;
        }

        if (Settings.CustomBlockRules is null)
        {
            Settings.CustomBlockRules = [];
            changed = true;
        }

        List<string> normalizedWorkspaces = (Settings.Workspaces ?? [])
            .Where(workspace => !string.IsNullOrWhiteSpace(workspace))
            .Select(workspace => workspace.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxWorkspaces)
            .ToList();

        if (normalizedWorkspaces.Count == 0)
            normalizedWorkspaces = CreateDefaultWorkspaces();

        if (Settings.Workspaces is null ||
            !Settings.Workspaces.SequenceEqual(normalizedWorkspaces, StringComparer.Ordinal))
        {
            Settings.Workspaces = normalizedWorkspaces;
            changed = true;
        }

        bool activeWorkspaceIsValid =
            !string.IsNullOrWhiteSpace(Settings.ActiveWorkspace) &&
            Settings.Workspaces.Contains(
                Settings.ActiveWorkspace,
                StringComparer.OrdinalIgnoreCase);

        if (!activeWorkspaceIsValid)
        {
            Settings.ActiveWorkspace = Settings.Workspaces[0];
            changed = true;
        }

        return changed;
    }

    private static List<string> CreateDefaultWorkspaces() =>
        ["Main", "Gaming", "Work"];

    private T Load<T>(string path, T fallback)
    {
        foreach (string candidate in GetLoadCandidates(path))
        {
            if (TryReadJson(candidate, out T? value) && value is not null)
                return value;
        }

        return fallback;
    }

    private static IEnumerable<string> GetLoadCandidates(string path)
    {
        yield return path;
        yield return path + BackupSuffix;
    }

    private static bool TryReadJson<T>(string path, out T? value)
    {
        value = default;

        if (!File.Exists(path))
            return false;

        try
        {
            string json = File.ReadAllText(path);
            value = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool Save<T>(string path, T value)
    {
        string temporaryPath = path + TemporarySuffix;
        string backupPath = path + BackupSuffix;

        try
        {
            string json = JsonSerializer.Serialize(value, JsonOptions);

            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (StreamWriter writer = new(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            ReplaceFile(temporaryPath, path, backupPath);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            DeleteFileIfExists(temporaryPath);
        }
    }

    private void ReplaceFile(
        string temporaryPath,
        string destinationPath,
        string backupPath)
    {
        if (!File.Exists(destinationPath))
        {
            File.Move(temporaryPath, destinationPath);
            return;
        }

        if (Settings.DataBackupsEnabled)
        {
            File.Replace(
                temporaryPath,
                destinationPath,
                backupPath,
                ignoreMetadataErrors: true);
            return;
        }

        File.Move(temporaryPath, destinationPath, overwrite: true);
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private Dictionary<string, string> LoadFavicons()
    {
        Dictionary<string, string> stored = Load(
            _faviconsPath,
            new Dictionary<string, string>());

        return stored
            .Where(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) &&
                IsValidFaviconImage(pair.Value))
            .TakeLast(MaxFavicons)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
    }

    public string GetFavicon(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        lock (_sync)
            return _favicons.GetValueOrDefault(url, string.Empty);
    }

    public void CacheFavicon(string? url, string? image)
    {
        if (!IsHttpUrl(url) || !IsValidFaviconImage(image))
            return;

        lock (_sync)
        {
            if (string.Equals(
                    _favicons.GetValueOrDefault(url),
                    image,
                    StringComparison.Ordinal))
            {
                return;
            }

            _favicons.Remove(url);
            _favicons[url] = image;

            while (_favicons.Count > MaxFavicons)
                _favicons.Remove(_favicons.Keys.First());

            Save(_faviconsPath, _favicons);
        }
    }

    private static bool IsValidFaviconImage([NotNullWhen(true)] string? image) =>
        !string.IsNullOrEmpty(image) &&
        image.Length <= MaxFaviconBytes &&
        image.StartsWith(FaviconDataPrefix, StringComparison.Ordinal);

    private static bool IsHttpUrl([NotNullWhen(true)] string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsBookmarked(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        lock (_sync)
        {
            return Bookmarks.Any(bookmark =>
                UrlEquals(bookmark.Url, url));
        }
    }

    public bool ToggleBookmark(string? title, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        lock (_sync)
        {
            BrowserBookmark? existing = Bookmarks.FirstOrDefault(bookmark =>
                UrlEquals(bookmark.Url, url));

            if (existing is not null)
            {
                Bookmarks.Remove(existing);
                Save(_bookmarksPath, Bookmarks);
                return false;
            }

            Bookmarks.Insert(0, new BrowserBookmark
            {
                Title = string.IsNullOrWhiteSpace(title) ? url : title,
                Url = url,
                Folder = DefaultBookmarkFolder,
                CreatedAt = DateTimeOffset.Now
            });

            Save(_bookmarksPath, Bookmarks);
            return true;
        }
    }

    public void RemoveBookmark(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        lock (_sync)
        {
            int removed = Bookmarks.RemoveAll(bookmark =>
                UrlEquals(bookmark.Url, url));

            if (removed > 0)
                Save(_bookmarksPath, Bookmarks);
        }
    }

    public int MergeBookmarks(IEnumerable<BrowserBookmark> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        lock (_sync)
        {
            HashSet<string> knownUrls = new(
                Bookmarks
                    .Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.Url))
                    .Select(bookmark => bookmark.Url),
                StringComparer.OrdinalIgnoreCase);

            int added = 0;

            foreach (BrowserBookmark bookmark in incoming)
            {
                if (string.IsNullOrWhiteSpace(bookmark.Url) ||
                    !knownUrls.Add(bookmark.Url))
                {
                    continue;
                }

                Bookmarks.Add(bookmark);
                added++;
            }

            if (added == 0)
                return 0;

            Bookmarks = Bookmarks
                .OrderByDescending(bookmark => bookmark.CreatedAt)
                .ToList();

            Save(_bookmarksPath, Bookmarks);
            return added;
        }
    }

    public void AddHistory(
        string? title,
        string? url,
        DateTimeOffset? when = null,
        int visitIncrement = 1)
    {
        if (!IsHttpUrl(url))
            return;

        DateTimeOffset visitedAt = when ?? DateTimeOffset.Now;
        int increment = Math.Max(1, visitIncrement);

        lock (_sync)
        {
            HistoryEntry? existing = History.FirstOrDefault(entry =>
                UrlEquals(entry.Url, url));

            if (existing is null)
            {
                History.Add(new HistoryEntry
                {
                    Title = string.IsNullOrWhiteSpace(title) ? url : title,
                    Url = url,
                    LastVisited = visitedAt,
                    VisitCount = increment
                });
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(title))
                    existing.Title = title;

                existing.LastVisited = visitedAt;
                existing.VisitCount = Math.Max(1, existing.VisitCount + increment);
            }

            TrimHistory();
            Save(_historyPath, History);
        }
    }

    public int MergeHistory(IEnumerable<HistoryEntry> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        lock (_sync)
        {
            Dictionary<string, HistoryEntry> historyByUrl = new(
                StringComparer.OrdinalIgnoreCase);

            foreach (HistoryEntry entry in History)
            {
                if (!string.IsNullOrWhiteSpace(entry.Url))
                    historyByUrl.TryAdd(entry.Url, entry);
            }

            int added = 0;

            foreach (HistoryEntry item in incoming)
            {
                if (string.IsNullOrWhiteSpace(item.Url))
                    continue;

                if (historyByUrl.TryGetValue(item.Url, out HistoryEntry? existing))
                {
                    MergeHistoryEntry(existing, item);
                    continue;
                }

                History.Add(item);
                historyByUrl[item.Url] = item;
                added++;
            }

            TrimHistory();
            Save(_historyPath, History);
            return added;
        }
    }

    private static void MergeHistoryEntry(
        HistoryEntry existing,
        HistoryEntry incoming)
    {
        if (incoming.LastVisited > existing.LastVisited)
        {
            existing.LastVisited = incoming.LastVisited;

            if (!string.IsNullOrWhiteSpace(incoming.Title))
                existing.Title = incoming.Title;
        }

        existing.VisitCount = Math.Max(
            existing.VisitCount,
            incoming.VisitCount);
    }

    private void TrimHistory()
    {
        History = History
            .OrderByDescending(entry => entry.LastVisited)
            .Take(MaxHistoryEntries)
            .ToList();
    }

    public void RemoveHistory(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        lock (_sync)
        {
            bool historyChanged = History.RemoveAll(entry =>
                UrlEquals(entry.Url, url)) > 0;

            bool faviconChanged = _favicons.Remove(url);

            if (faviconChanged)
                Save(_faviconsPath, _favicons);

            if (historyChanged)
                Save(_historyPath, History);
        }
    }

    public void ClearHistory()
    {
        lock (_sync)
        {
            History.Clear();
            _favicons.Clear();

            Save(_faviconsPath, _favicons);
            DeleteFileIfExists(_faviconsPath + BackupSuffix);
            Save(_historyPath, History);
        }
    }

    public DownloadEntry AddDownload(
        string? filePath,
        string? sourceUrl)
    {
        lock (_sync)
        {
            DownloadEntry entry = new()
            {
                FileName = string.IsNullOrWhiteSpace(filePath)
                    ? "Download"
                    : Path.GetFileName(filePath),
                FilePath = filePath ?? string.Empty,
                SourceUrl = sourceUrl ?? string.Empty,
                State = DownloadInProgressState,
                StartedAt = DateTimeOffset.Now
            };

            Downloads.Insert(0, entry);
            TrimDownloads();

            Save(_downloadsPath, Downloads);
            return entry;
        }
    }

    public void UpdateDownload(
        string id,
        string state,
        string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        lock (_sync)
        {
            DownloadEntry? entry = Downloads.FirstOrDefault(download =>
                string.Equals(download.Id, id, StringComparison.Ordinal));

            if (entry is null)
                return;

            entry.State = state;

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                entry.FilePath = filePath;
                entry.FileName = Path.GetFileName(filePath);
            }

            if (!string.Equals(
                    state,
                    DownloadInProgressState,
                    StringComparison.OrdinalIgnoreCase))
            {
                entry.CompletedAt = DateTimeOffset.Now;
            }

            Save(_downloadsPath, Downloads);
        }
    }

    private void TrimDownloads()
    {
        if (Downloads.Count <= MaxDownloadEntries)
            return;

        Downloads.RemoveRange(
            MaxDownloadEntries,
            Downloads.Count - MaxDownloadEntries);
    }

    public void RemoveDownload(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        lock (_sync)
        {
            int removed = Downloads.RemoveAll(download =>
                string.Equals(download.Id, id, StringComparison.Ordinal));

            if (removed > 0)
                Save(_downloadsPath, Downloads);
        }
    }

    public void ClearDownloads()
    {
        lock (_sync)
        {
            if (Downloads.Count == 0)
                return;

            Downloads.Clear();
            Save(_downloadsPath, Downloads);
        }
    }

    public void SaveSettings()
    {
        lock (_sync)
            Save(_settingsPath, Settings);
    }

    public SessionState LoadSessionState()
    {
        foreach (string candidate in GetLoadCandidates(_sessionPath))
        {
            if (TryReadJson(candidate, out SessionState? state) &&
                state is not null)
            {
                return NormalizeSessionState(state);
            }

            if (TryReadJson(candidate, out List<string>? legacy) &&
                legacy is not null)
            {
                return NormalizeSessionState(new SessionState
                {
                    Tabs = legacy,
                    ActiveIndex = 0
                });
            }
        }

        return NormalizeSessionState(new SessionState());
    }

    private static SessionState NormalizeSessionState(SessionState state)
    {
        state.Tabs ??= [];
        state.TabStates ??= [];

        state.Tabs = state.Tabs
            .Take(MaxSessionTabs)
            .ToList();

        state.TabStates = state.TabStates
            .Take(MaxSessionTabs)
            .ToList();

        int tabCount = state.TabStates.Count > 0
            ? state.TabStates.Count
            : state.Tabs.Count;

        state.ActiveIndex = tabCount == 0
            ? 0
            : Math.Clamp(state.ActiveIndex, 0, tabCount - 1);

        return state;
    }

    public bool SaveSessionState(
        IEnumerable<SessionTabState> tabStates,
        int activeIndex)
    {
        ArgumentNullException.ThrowIfNull(tabStates);

        lock (_sync)
        {
            List<SessionTabState> states = tabStates
                .Take(MaxSessionTabs)
                .ToList();

            SessionState state = new()
            {
                Tabs = states
                    .Select(tab => tab.Address)
                    .ToList(),
                TabStates = states,
                ActiveIndex = states.Count == 0
                    ? 0
                    : Math.Clamp(activeIndex, 0, states.Count - 1),
                SavedAt = DateTimeOffset.Now
            };

            return Save(_sessionPath, state);
        }
    }

    public void SaveSessionState(
        IEnumerable<string> addresses,
        int activeIndex)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        SaveSessionState(
            addresses.Select(address => new SessionTabState
            {
                Address = address
            }),
            activeIndex);
    }

    public List<string> LoadSession()
    {
        SessionState state = LoadSessionState();

        return state.TabStates.Count > 0
            ? state.TabStates.Select(tab => tab.Address).ToList()
            : state.Tabs;
    }

    public void SaveSession(IEnumerable<string> addresses)
    {
        SaveSessionState(addresses, 0);
    }

    private static bool UrlEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
