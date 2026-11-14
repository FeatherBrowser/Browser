using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading;
using FeatherBrowser.Domain.Models;

namespace FeatherBrowser.Infrastructure.Persistence;

internal sealed class BrowserDataStore : IDisposable
{
    private static readonly Lazy<BrowserDataStore> SharedInstance = new(() => new BrowserDataStore());
    public static BrowserDataStore Shared => SharedInstance.Value;

    private const string ApplicationDirectoryName = "FeatherBrowser";
    private const string DataDirectoryName = "Data";

    private const string BackupSuffix = ".bak";
    private const string TemporarySuffix = ".tmp";

    private const string DefaultBookmarkFolder = "Favorites";
    private const string DownloadInProgressState = "In progress";
    private const string DownloadInterruptedState = "Interrupted";

    private const int MaxBookmarks = 5_000;
    private const int MaxFavicons = 256;
    private const int MaxFaviconBytes = 65_536;
    private const int MaxHistoryEntries = 3_000;
    private const int MaxDownloadEntries = 500;
    private const int MaxSessionTabs = 40;
    private const int MaxWorkspaces = 12;

    private const int SchemaVersion4 = 4;
    private const int SchemaVersion5 = 5;
    private const int SchemaVersion6 = 6;
    private const int SchemaVersion7 = 7;
    private const int SchemaVersion8 = 8;

    private const string FaviconDataPrefix = "data:image/png;base64,";
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private const long MaxJsonFileBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly TimeSpan DeferredSaveDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FailedSaveRetryDelay = TimeSpan.FromSeconds(10);

    private readonly object _sync = new();
    private readonly Timer _deferredSaveTimer;
    private bool _historyDirty;
    private bool _faviconsDirty;
    private bool _disposed;

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
    public IReadOnlyList<DeviceTabs> OtherDeviceTabs { get; set; } = [];
    public string InstallationId
    {
        get
        {
            bool saved = false;
            string installationId;

            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(Settings.InstallationId))
                {
                    Settings.InstallationId = Guid.NewGuid().ToString("N");
                    saved = Save(_settingsPath, Settings);
                }

                installationId = Settings.InstallationId;
            }

            if (saved)
                SettingsSaved?.Invoke();

            return installationId;
        }
    }

    public BrowserDataStore()
    {
        _root = Path.Join(
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

        _hadExistingSettings = File.Exists(_settingsPath) ||
                               File.Exists(_settingsPath + BackupSuffix);

        Settings = Load(_settingsPath, new BrowserSettings());
        Bookmarks = Load(_bookmarksPath, new List<BrowserBookmark>());
        History = Load(_historyPath, new List<HistoryEntry>());
        Downloads = Load(_downloadsPath, new List<DownloadEntry>());
        _favicons = LoadFavicons();

        Bookmarks = Bookmarks
            .Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.Url))
            .Take(MaxBookmarks)
            .ToList();
        TrimHistory();
        TrimDownloads();

        _deferredSaveTimer = new Timer(
            static state => ((BrowserDataStore)state!).FlushDeferredSaves(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        MigrateSettings();
        ReconcileInterruptedDownloads();
    }

    private void ReconcileInterruptedDownloads()
    {
        bool changed = false;

        foreach (DownloadEntry entry in Downloads.Where(entry =>
                     string.Equals(entry.State, DownloadInProgressState,
                         StringComparison.OrdinalIgnoreCase)))
        {
            entry.State = DownloadInterruptedState;
            entry.CompletedAt ??= DateTimeOffset.Now;
            changed = true;
        }

        if (changed)
            Save(_downloadsPath, Downloads);
    }

    private string GetDataPath(string fileName) => Path.Join(_root, fileName);

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

        if (Settings.SettingsSchemaVersion < SchemaVersion7)
        {
            MigrateToSchema7();
            changed = true;
        }

        if (Settings.SettingsSchemaVersion < SchemaVersion8)
        {
            MigrateToSchema8();
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

    private void MigrateToSchema7()
    {
        Settings.SitePermissions = new Dictionary<string, string>();
        Settings.SettingsSchemaVersion = SchemaVersion7;
    }

    private void MigrateToSchema8()
    {
        bool onForcedSaverPolicy =
            Settings.MaxLoadedTabs <= 1 &&
            Settings.UnloadAfterSeconds <= 5 &&
            Settings.SleepAfterSeconds <= 2 &&
            !Settings.GameMode;

        if (onForcedSaverPolicy)
            FeatherBrowser.Features.WebView3.PerformancePolicy.ApplyProfile(Settings, "balanced");

        Settings.SettingsSchemaVersion = SchemaVersion8;
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

        int normalizedSyncMinutes = Math.Clamp(Settings.AutoSyncMinutes, 1, 60);
        if (Settings.AutoSyncMinutes != normalizedSyncMinutes)
        {
            Settings.AutoSyncMinutes = normalizedSyncMinutes;
            changed = true;
        }

        return changed;
    }

    private static List<string> CreateDefaultWorkspaces() =>
        ["Main", "Gaming", "Work"];

    private T Load<T>(string path, T fallback)
    {
        if (TryReadJson(path, out T? primary) && primary is not null)
            return primary;

        if (TryReadJson(path + BackupSuffix, out T? backup) && backup is not null)
        {
            TryRestoreBackup(path);
            return backup;
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

        try
        {
            using FileStream stream = new(
                path, FileMode.Open, FileAccess.Read, FileShare.Read);

            if (stream.Length > MaxJsonFileBytes)
            {
                Debug.WriteLine($"Skipping oversized browser data file: {path}");
                return false;
            }

            value = JsonSerializer.Deserialize<T>(stream, JsonOptions);
            return value is not null;
        }
        catch (Exception ex) when (ex is JsonException or
                                   NotSupportedException or
                                   IOException or
                                   UnauthorizedAccessException)
        {
            Debug.WriteLine($"Cannot read browser data file '{path}': {ex.Message}");
            return false;
        }
    }

    private static void TryRestoreBackup(string destinationPath)
    {
        string backupPath = destinationPath + BackupSuffix;
        string restorePath = destinationPath + TemporarySuffix + "." +
                             Guid.NewGuid().ToString("N");

        try
        {
            File.Copy(backupPath, restorePath);

            if (File.Exists(destinationPath))
            {
                File.Replace(restorePath, destinationPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(restorePath, destinationPath);
            }
        }
        catch (Exception ex) when (ex is IOException or
                                   UnauthorizedAccessException)
        {
            Debug.WriteLine($"Failed to restore '{destinationPath}': {ex.Message}");
        }
        finally
        {
            DeleteFileIfExists(restorePath);
        }
    }

    public bool LastSaveSucceeded { get; private set; } = true;

    private bool Save<T>(string path, T value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool saved = WriteJson(path, value);
        LastSaveSucceeded = saved;
        return saved;
    }

    private bool WriteJson<T>(string path, T value)
    {
        string temporaryPath = path + TemporarySuffix + "." +
                               Guid.NewGuid().ToString("N");
        string backupPath = path + BackupSuffix;

        try
        {
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16_384,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            ReplaceFile(temporaryPath, path, backupPath);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or
                                   NotSupportedException or
                                   IOException or
                                   UnauthorizedAccessException)
        {
            Debug.WriteLine($"Failed to save browser data '{path}': {ex.Message}");
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

        File.Replace(
            temporaryPath,
            destinationPath,
            Settings.DataBackupsEnabled ? backupPath : null,
            ignoreMetadataErrors: true);

        if (!Settings.DataBackupsEnabled)
            DeleteFileIfExists(backupPath);
    }

    private static bool DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);

            return true;
        }
        catch (IOException exception)
        {
            Debug.WriteLine($"Failed to delete file '{path}': {exception.Message}");
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.WriteLine($"Access denied deleting file '{path}': {exception.Message}");
            return false;
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
            ObjectDisposedException.ThrowIf(_disposed, this);

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

            _faviconsDirty = true;
            ScheduleDeferredSave();
        }
    }

    private static bool IsValidFaviconImage([NotNullWhen(true)] string? image)
    {
        if (string.IsNullOrEmpty(image) ||
            !image.StartsWith(FaviconDataPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        int base64Length = image.Length - FaviconDataPrefix.Length;
        int maxBase64Length = ((MaxFaviconBytes + 2) / 3) * 4;

        if (base64Length < 32 || base64Length > maxBase64Length)
            return false;

        try
        {
            byte[] decoded = Convert.FromBase64String(image[FaviconDataPrefix.Length..]);
            return decoded.Length <= MaxFaviconBytes &&
                   decoded.Length >= 24 &&
                   decoded.AsSpan().StartsWith(PngSignature) &&
                   decoded.AsSpan(12, 4).SequenceEqual("IHDR"u8);
        }
        catch (FormatException)
        {
            return false;
        }
    }

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

    public bool ReplaceBookmarks(IEnumerable<BrowserBookmark> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        lock (_sync)
        {
            Bookmarks = incoming
                .Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.Url))
                .GroupBy(bookmark => bookmark.Url, UrlIdentity.Comparer)
                .Select(group => group.OrderByDescending(bookmark => bookmark.CreatedAt).First())
                .OrderByDescending(bookmark => bookmark.CreatedAt)
                .Take(MaxBookmarks)
                .ToList();

            return Save(_bookmarksPath, Bookmarks);
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
                UrlIdentity.Comparer);

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
                .Take(MaxBookmarks)
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
            ObjectDisposedException.ThrowIf(_disposed, this);

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
                existing.VisitCount = (int)Math.Min(
                    int.MaxValue, (long)Math.Max(1, existing.VisitCount) + increment);
            }

            TrimHistory();
            _historyDirty = true;
            ScheduleDeferredSave();
        }
    }

    private void ScheduleDeferredSave() =>
        _deferredSaveTimer.Change(DeferredSaveDelay, Timeout.InfiniteTimeSpan);

    public void FlushDeferredSaves()
    {
        lock (_sync)
        {
            if (!_disposed)
                FlushDeferredSavesCore(retryOnFailure: true);
        }
    }

    private void FlushDeferredSavesCore(bool retryOnFailure)
    {
        if (_historyDirty && Save(_historyPath, History))
            _historyDirty = false;

        if (_faviconsDirty && Save(_faviconsPath, _favicons))
            _faviconsDirty = false;

        if (retryOnFailure && (_historyDirty || _faviconsDirty))
        {
            _deferredSaveTimer.Change(
                FailedSaveRetryDelay,
                Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _deferredSaveTimer.Change(
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);

            FlushDeferredSavesCore(retryOnFailure: false);
            _disposed = true;
        }

        _deferredSaveTimer.Dispose();
        GC.SuppressFinalize(this);
    }

    public bool ReplaceHistory(IEnumerable<HistoryEntry> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        lock (_sync)
        {
            History = incoming
                .Where(entry => IsHttpUrl(entry.Url))
                .GroupBy(entry => entry.Url, UrlIdentity.Comparer)
                .Select(group => group.OrderByDescending(entry => entry.LastVisited).First())
                .OrderByDescending(entry => entry.LastVisited)
                .Take(MaxHistoryEntries)
                .ToList();

            bool saved = Save(_historyPath, History);
            _historyDirty = !saved;
            if (!saved)
                ScheduleDeferredSave();
            return saved;
        }
    }

    public int MergeHistory(IEnumerable<HistoryEntry> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        lock (_sync)
        {
            Dictionary<string, HistoryEntry> historyByUrl = new(
                UrlIdentity.Comparer);

            foreach (HistoryEntry entry in History.Where(entry => IsHttpUrl(entry.Url)))
            {
                historyByUrl.TryAdd(entry.Url, entry);
            }

            int added = 0;

            foreach (HistoryEntry item in incoming.Where(item => IsHttpUrl(item.Url)))
            {
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
            _historyDirty = !Save(_historyPath, History);
            if (_historyDirty)
                ScheduleDeferredSave();
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
            1,
            Math.Max(existing.VisitCount, incoming.VisitCount));
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

            if (historyChanged)
            {
                _historyDirty = !Save(_historyPath, History);
                if (!_historyDirty)
                    DeleteFileIfExists(_historyPath + BackupSuffix);
            }

            if (faviconChanged)
            {
                _faviconsDirty = !Save(_faviconsPath, _favicons);
                if (!_faviconsDirty)
                    DeleteFileIfExists(_faviconsPath + BackupSuffix);
            }

            if (_historyDirty || _faviconsDirty)
                ScheduleDeferredSave();
        }
    }

    public bool ClearHistory()
    {
        lock (_sync)
        {
            bool historySaved = Save(_historyPath, Array.Empty<HistoryEntry>());
            bool faviconsSaved = Save(
                _faviconsPath,
                new Dictionary<string, string>(StringComparer.Ordinal));

            if (historySaved)
            {
                History.Clear();
                _historyDirty = false;
            }

            if (faviconsSaved)
            {
                _favicons.Clear();
                _faviconsDirty = false;
            }

            bool historyBackupRemoved = historySaved &&
                DeleteFileIfExists(_historyPath + BackupSuffix);
            bool faviconBackupRemoved = faviconsSaved &&
                DeleteFileIfExists(_faviconsPath + BackupSuffix);

            return historySaved && faviconsSaved &&
                   historyBackupRemoved && faviconBackupRemoved;
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

    public event Action? SettingsSaved;

    public bool SaveSettings()
    {
        bool saved;
        lock (_sync)
            saved = Save(_settingsPath, Settings);
        if (saved)
            SettingsSaved?.Invoke();
        return saved;
    }

    public SessionState LoadSessionState()
    {
        foreach (string candidate in GetLoadCandidates(_sessionPath))
        {
            if (TryReadJson(candidate, out SessionState? state) &&
                state is not null)
            {
                if (!string.Equals(candidate, _sessionPath, StringComparison.Ordinal))
                    TryRestoreBackup(_sessionPath);

                return NormalizeSessionState(state);
            }

            if (TryReadJson(candidate, out List<string>? legacy) &&
                legacy is not null)
            {
                if (!string.Equals(candidate, _sessionPath, StringComparison.Ordinal))
                    TryRestoreBackup(_sessionPath);

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
        UrlIdentity.Equals(left, right);
}
