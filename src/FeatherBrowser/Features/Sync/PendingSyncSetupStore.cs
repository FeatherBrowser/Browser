using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FeatherBrowser.Features.Sync;

internal sealed class PendingSyncSetupStore
{
    private readonly string _path;

    public PendingSyncSetupStore()
    {
        string root = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FeatherBrowser",
            "Data");

        Directory.CreateDirectory(root);
        _path = Path.Join(root, "sync-setup-pending.bin");
    }

    public void Save(string userId, ReadOnlySpan<byte> masterKey, string recoveryCode, string fingerprint)
    {
        var record = new PendingSyncSetup
        {
            UserId = userId,
            MasterKey = Convert.ToBase64String(masterKey),
            RecoveryCode = recoveryCode,
            Fingerprint = fingerprint
        };

        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(record);
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

    public PendingSyncSetup? Load(string userId)
    {
        if (!File.Exists(_path))
            return null;

        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            PendingSyncSetup? record = JsonSerializer.Deserialize<PendingSyncSetup>(plain);
            return record is not null && string.Equals(record.UserId, userId, StringComparison.OrdinalIgnoreCase)
                ? record
                : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (plain is not null)
                CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not remove pending sync setup: {ex.Message}");
        }
    }
}

internal sealed class PendingSyncSetup
{
    public string UserId { get; init; } = "";
    public string MasterKey { get; init; } = "";
    public string RecoveryCode { get; init; } = "";
    public string Fingerprint { get; init; } = "";
}
