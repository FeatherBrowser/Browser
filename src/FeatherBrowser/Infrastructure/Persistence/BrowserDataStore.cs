using System.IO;
using System.Text.Json;
using FeatherBrowser.Domain.Models;

namespace FeatherBrowser.Infrastructure.Persistence;

internal sealed class BrowserDataStore
{
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _root;
    private readonly string _bookmarksPath;
    private readonly string _historyPath;
    private readonly string _settingsPath;
    private readonly string _sessionPath;
    private readonly string _downloadsPath;
    private readonly bool _hadExistingSettings;
    private readonly string _faviconsPath;
    private readonly Dictionary<string, string> _favicons;

    public List<BrowserBookmark> Bookmarks { get; private set; }
    public List<HistoryEntry> History { get; private set; }
    public BrowserSettings Settings { get; private set; }
    public List<DownloadEntry> Downloads { get; private set; }

    public BrowserDataStore()
    {
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FeatherBrowser", "Data");
        Directory.CreateDirectory(_root);
        _bookmarksPath = Path.Combine(_root, "bookmarks.json");
        _historyPath = Path.Combine(_root, "history.json");
        _settingsPath = Path.Combine(_root, "settings.json");
        _sessionPath = Path.Combine(_root, "session.json");
        _downloadsPath = Path.Combine(_root, "downloads.json");
        _hadExistingSettings = File.Exists(_settingsPath);

        Bookmarks = Load(_bookmarksPath, new List<BrowserBookmark>());
        History = Load(_historyPath, new List<HistoryEntry>());
        Settings = Load(_settingsPath, new BrowserSettings());
        Downloads = Load(_downloadsPath, new List<DownloadEntry>());
        _faviconsPath = Path.Join(_root, "favicons.json");
        _favicons = Load(_faviconsPath, new Dictionary<string, string>()).Where(pair => pair.Value is not null && pair.Value.Length <= 65536 && pair.Value.StartsWith("data:image/png;base64,", StringComparison.Ordinal)).TakeLast(256).ToDictionary(pair => pair.Key, pair => pair.Value);
        MigrateSettings();
    }

    private void MigrateSettings()
    {
        bool changed = false;

        if (Settings.SettingsSchemaVersion < 4)
        {
            Settings.MaxLoadedTabs = 1;
            Settings.UnloadAfterSeconds = Math.Min(Settings.UnloadAfterSeconds <= 0 ? 5 : Settings.UnloadAfterSeconds, 5);
            Settings.SleepAfterSeconds = Math.Min(Settings.SleepAfterSeconds <= 0 ? 2 : Settings.SleepAfterSeconds, 2);
            Settings.LowMemoryMode = true;
            Settings.EcoMode = true;
            Settings.AutoMemoryGuard = true;
            Settings.MemoryGuardMb = 700;
            Settings.HibernateWhenMinimized = true;
            Settings.CrashRecoveryAutosave = true;
            Settings.ShowWorkspaceSidebar = true;
            Settings.AdaptiveMemoryMode = true;
            Settings.ColdUnloadOtherWorkspaces = true;
            Settings.Workspaces ??= ["Main", "Gaming", "Work"];
            if (Settings.Workspaces.Count == 0)
                Settings.Workspaces = ["Main", "Gaming", "Work"];
            if (string.IsNullOrWhiteSpace(Settings.ActiveWorkspace) || !Settings.Workspaces.Contains(Settings.ActiveWorkspace, StringComparer.OrdinalIgnoreCase))
                Settings.ActiveWorkspace = Settings.Workspaces[0];
            Settings.StartupMode = Settings.RestorePreviousSession ? "Restore" : "NewTab";
            Settings.SettingsSchemaVersion = 4;
            changed = true;
        }

        if (Settings.SettingsSchemaVersion < 5)
        {
            Settings.SitePermissions ??= new Dictionary<string, string>();
            Settings.BlockNotificationPrompts = true;
            Settings.SendDoNotTrack = true;
            Settings.SettingsSchemaVersion = 5;
            changed = true;
        }

        if (Settings.SettingsSchemaVersion < 6)
        {
            Settings.FirstRunCompleted = _hadExistingSettings;
            Settings.DataBackupsEnabled = true;
            Settings.SettingsSchemaVersion = 6;
            changed = true;
        }

        Settings.SitePermissions ??= new Dictionary<string, string>();
        Settings.Workspaces ??= ["Main", "Gaming", "Work"];
        Settings.KeepAliveSites ??= [];
        Settings.AllowlistedSites ??= [];
        Settings.CustomBlockRules ??= [];

        if (changed)
            Save(_settingsPath, Settings);
    }

    private T Load<T>(string path, T fallback)
    {
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate))
                    continue;
                T? value = JsonSerializer.Deserialize<T>(File.ReadAllText(candidate), _jsonOptions);
                if (value is not null)
                    return value;
            }
            catch
            {
            }
        }
        return fallback;
    }

    private bool Save<T>(string path, T value)
    {
        string temp = path + ".tmp";
        string backup = path + ".bak";
        try
        {
            if (Settings.DataBackupsEnabled && File.Exists(path))
                File.Copy(path, backup, true);

            string json = JsonSerializer.Serialize(value, _jsonOptions);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temp, path, true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch { }
        }
    }

    public string GetFavicon(string url)
{
    lock (_sync)
        return _favicons.GetValueOrDefault(url, string.Empty);
}

public void CacheFavicon(string url, string image)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
        (uri.Scheme != Uri.UriSchemeHttp &&
         uri.Scheme != Uri.UriSchemeHttps) ||
        image.Length > 65536 ||
        !image.StartsWith(
            "data:image/png;base64,",
            StringComparison.Ordinal))
        return;

    lock (_sync)
    {
        if (_favicons.GetValueOrDefault(url) == image)
            return;

        _favicons.Remove(url);
        _favicons[url] = image;

        while (_favicons.Count > 256)
            _favicons.Remove(_favicons.Keys.First());

        Save(_faviconsPath, _favicons);
    }
}

    public bool IsBookmarked(string url)
    {
        lock (_sync)
            return Bookmarks.Any(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));
    }

    public bool ToggleBookmark(string title, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        lock (_sync)
        {
            var existing = Bookmarks.FirstOrDefault(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));
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
                Folder = "Favorites",
                CreatedAt = DateTimeOffset.Now
            });
            Save(_bookmarksPath, Bookmarks);
            return true;
        }
    }

    public void RemoveBookmark(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        lock (_sync)
        {
            Bookmarks.RemoveAll(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));
            Save(_bookmarksPath, Bookmarks);
        }
    }

    public int MergeBookmarks(IEnumerable<BrowserBookmark> incoming)
    {
        lock (_sync)
        {
            int added = 0;
            var known = new HashSet<string>(Bookmarks.Select(x => x.Url), StringComparer.OrdinalIgnoreCase);
            foreach (var item in incoming)
            {
                if (string.IsNullOrWhiteSpace(item.Url) || !known.Add(item.Url))
                    continue;
                Bookmarks.Add(item);
                added++;
            }
            Bookmarks = Bookmarks.OrderByDescending(x => x.CreatedAt).ToList();
            Save(_bookmarksPath, Bookmarks);
            return added;
        }
    }

    public void AddHistory(string title, string url, DateTimeOffset? when = null, int visitIncrement = 1)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return;

        lock (_sync)
        {
            var existing = History.FirstOrDefault(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                History.Add(new HistoryEntry
                {
                    Title = string.IsNullOrWhiteSpace(title) ? url : title,
                    Url = url,
                    LastVisited = when ?? DateTimeOffset.Now,
                    VisitCount = Math.Max(1, visitIncrement)
                });
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(title))
                    existing.Title = title;
                existing.LastVisited = when ?? DateTimeOffset.Now;
                existing.VisitCount = Math.Max(1, existing.VisitCount + Math.Max(1, visitIncrement));
            }

            History = History
                .OrderByDescending(x => x.LastVisited)
                .Take(3000)
                .ToList();
            Save(_historyPath, History);
        }
    }

    public int MergeHistory(IEnumerable<HistoryEntry> incoming)
    {
        lock (_sync)
        {
            int added = 0;
            var byUrl = History.ToDictionary(x => x.Url, StringComparer.OrdinalIgnoreCase);
            foreach (var item in incoming)
            {
                if (string.IsNullOrWhiteSpace(item.Url))
                    continue;

                if (byUrl.TryGetValue(item.Url, out var existing))
                {
                    if (item.LastVisited > existing.LastVisited)
                    {
                        existing.LastVisited = item.LastVisited;
                        existing.Title = string.IsNullOrWhiteSpace(item.Title) ? existing.Title : item.Title;
                    }
                    existing.VisitCount = Math.Max(existing.VisitCount, item.VisitCount);
                }
                else
                {
                    History.Add(item);
                    byUrl[item.Url] = item;
                    added++;
                }
            }

            History = History.OrderByDescending(x => x.LastVisited).Take(3000).ToList();
            Save(_historyPath, History);
            return added;
        }
    }

    public void RemoveHistory(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        lock (_sync)
        {
            History.RemoveAll(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));
            _favicons.Remove(url);
            Save(_faviconsPath, _favicons);
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
            if (File.Exists(_faviconsPath + ".bak")) File.Delete(_faviconsPath + ".bak");
            Save(_historyPath, History);
        }
    }


    public DownloadEntry AddDownload(string filePath, string sourceUrl)
    {
        lock (_sync)
        {
            var entry = new DownloadEntry
            {
                FileName = string.IsNullOrWhiteSpace(filePath) ? "Download" : Path.GetFileName(filePath),
                FilePath = filePath ?? string.Empty,
                SourceUrl = sourceUrl ?? string.Empty,
                State = "In progress",
                StartedAt = DateTimeOffset.Now
            };
            Downloads.Insert(0, entry);
            if (Downloads.Count > 500)
                Downloads = Downloads.Take(500).ToList();
            Save(_downloadsPath, Downloads);
            return entry;
        }
    }

    public void UpdateDownload(string id, string state, string? filePath = null)
    {
        lock (_sync)
        {
            DownloadEntry? entry = Downloads.FirstOrDefault(x => x.Id == id);
            if (entry is null)
                return;

            entry.State = state;
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                entry.FilePath = filePath;
                entry.FileName = Path.GetFileName(filePath);
            }
            if (!string.Equals(state, "In progress", StringComparison.OrdinalIgnoreCase))
                entry.CompletedAt = DateTimeOffset.Now;
            Save(_downloadsPath, Downloads);
        }
    }

    public void RemoveDownload(string id)
    {
        lock (_sync)
        {
            Downloads.RemoveAll(x => x.Id == id);
            Save(_downloadsPath, Downloads);
        }
    }

    public void ClearDownloads()
    {
        lock (_sync)
        {
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
        foreach (string candidate in new[] { _sessionPath, _sessionPath + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate))
                    continue;

                string json = File.ReadAllText(candidate);
                SessionState? state = JsonSerializer.Deserialize<SessionState>(json, _jsonOptions);
                if (state is not null)
                {
                    state.Tabs ??= [];
                    state.TabStates ??= [];
                    state.Tabs = state.Tabs.Take(40).ToList();
                    state.TabStates = state.TabStates.Take(40).ToList();
                    int count = state.TabStates.Count > 0 ? state.TabStates.Count : state.Tabs.Count;
                    state.ActiveIndex = count == 0 ? 0 : Math.Clamp(state.ActiveIndex, 0, count - 1);
                    return state;
                }
            }
            catch
            {
                try
                {
                    List<string>? legacy = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(candidate), _jsonOptions);
                    if (legacy is not null)
                        return new SessionState { Tabs = legacy.Take(40).ToList(), ActiveIndex = 0 };
                }
                catch { }
            }
        }

        return new SessionState();
    }

    public bool SaveSessionState(IEnumerable<SessionTabState> tabStates, int activeIndex)
    {
        lock (_sync)
        {
            List<SessionTabState> states = tabStates.Take(40).ToList();
            return Save(_sessionPath, new SessionState
            {
                Tabs = states.Select(x => x.Address).ToList(),
                TabStates = states,
                ActiveIndex = states.Count == 0 ? 0 : Math.Clamp(activeIndex, 0, states.Count - 1),
                SavedAt = DateTimeOffset.Now
            });
        }
    }

    public void SaveSessionState(IEnumerable<string> addresses, int activeIndex) =>
        SaveSessionState(addresses.Select(x => new SessionTabState { Address = x }), activeIndex);

    public List<string> LoadSession() => LoadSessionState().TabStates.Count > 0
        ? LoadSessionState().TabStates.Select(x => x.Address).ToList()
        : LoadSessionState().Tabs;

    public void SaveSession(IEnumerable<string> addresses) => SaveSessionState(addresses, 0);
}

