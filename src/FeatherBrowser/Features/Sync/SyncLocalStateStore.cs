using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FeatherBrowser.Features.Sync.Models;

namespace FeatherBrowser.Features.Sync;

internal sealed class SyncLocalStateStore
{
    private readonly string _path;

    public SyncLocalStateStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FeatherBrowser",
            "Data");

        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "sync-state.bin");
    }

    public SyncLocalState? Load(string userId)
    {
        if (!Guid.TryParse(userId, out _))
            return null;

        if (!File.Exists(_path))
            return null;

        byte[] protectedBytes = File.ReadAllBytes(_path);
        byte[]? plain = null;

        try
        {
            plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            SyncLocalState? state = JsonSerializer.Deserialize<SyncLocalState>(plain);
            return state is not null && string.Equals(state.UserId, userId, StringComparison.OrdinalIgnoreCase)
                ? state
                : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (plain is not null)
                CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Save(string userId, long revision, SyncSnapshot snapshot, SyncSnapshotService snapshots)
    {
        SyncSectionHashes hashes = snapshots.Hash(snapshot);
        var state = new SyncLocalState
        {
            UserId = userId,
            Revision = revision,
            BookmarksHash = hashes.Bookmarks,
            SettingsHash = hashes.Settings,
            HistoryHash = hashes.History,
            SessionHash = hashes.Session,
            LastSyncUtc = DateTimeOffset.UtcNow
        };

        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(state);
        byte[]? protectedBytes = null;

        try
        {
            protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            string temp = _path + ".tmp";
            File.WriteAllBytes(temp, protectedBytes);
            File.Move(temp, _path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (protectedBytes is not null)
                CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class SyncLocalState
{
    public string UserId { get; init; } = "";
    public long Revision { get; init; }
    public string BookmarksHash { get; init; } = "";
    public string SettingsHash { get; init; } = "";
    public string HistoryHash { get; init; } = "";
    public string SessionHash { get; init; } = "";
    public DateTimeOffset LastSyncUtc { get; init; }
}
