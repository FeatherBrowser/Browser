using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FeatherBrowser.Features.Sync;

internal sealed class SyncDeviceSecretStore
{
    private static ReadOnlySpan<byte> Magic => "FTHRS001"u8;

    private const int MasterKeySize = 32;
    private const int FingerprintSize = 32;
    private const int PlainSize = 8 + 16 + 8 + FingerprintSize + MasterKeySize;

    private readonly string _path;

    public SyncDeviceSecretStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FeatherBrowser",
            "Data");

        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "sync-device.bin");
    }

    public void Save(
        string userId,
        ReadOnlySpan<byte> masterKey,
        string keyBundleFingerprint,
        long highestSeenRevision)
    {
        if (!Guid.TryParse(userId, out Guid userGuid))
            throw new SyncSecurityException("The Supabase user id is invalid.");

        if (masterKey.Length != MasterKeySize)
            throw new ArgumentException("A 256-bit master key is required.", nameof(masterKey));

        byte[] fingerprint;

        try
        {
            fingerprint = Convert.FromHexString(keyBundleFingerprint);
        }
        catch (FormatException exception)
        {
            throw new SyncSecurityException("The sync key fingerprint is invalid.", exception);
        }

        if (fingerprint.Length != FingerprintSize)
        {
            CryptographicOperations.ZeroMemory(fingerprint);
            throw new SyncSecurityException("The sync key fingerprint is invalid.");
        }

        byte[] plain = new byte[PlainSize];
        byte[]? protectedBytes = null;

        try
        {
            int offset = 0;
            Magic.CopyTo(plain.AsSpan(offset, Magic.Length));
            offset += Magic.Length;

            if (!userGuid.TryWriteBytes(plain.AsSpan(offset, 16)))
                throw new SyncSecurityException("Could not serialize the sync account id.");

            offset += 16;
            BinaryPrimitives.WriteInt64LittleEndian(
                plain.AsSpan(offset, 8),
                highestSeenRevision);

            offset += 8;
            fingerprint.CopyTo(plain, offset);
            offset += FingerprintSize;
            masterKey.CopyTo(plain.AsSpan(offset, MasterKeySize));

            protectedBytes = ProtectedData.Protect(
                plain,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            WriteAtomic(_path, protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fingerprint);
            CryptographicOperations.ZeroMemory(plain);

            if (protectedBytes is not null)
                CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public SyncDeviceSecrets? Load()
    {
        if (!File.Exists(_path))
            return null;

        byte[] protectedBytes = File.ReadAllBytes(_path);
        byte[]? plain = null;

        try
        {
            plain = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            if (plain.Length != PlainSize ||
                !plain.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                throw new SyncSecurityException("The local Feather sync secret file is invalid.");
            }

            int offset = Magic.Length;
            Guid userId = new(plain.AsSpan(offset, 16));
            offset += 16;

            long highestSeenRevision =
                BinaryPrimitives.ReadInt64LittleEndian(plain.AsSpan(offset, 8));

            offset += 8;

            string fingerprint =
                Convert.ToHexString(plain.AsSpan(offset, FingerprintSize));

            offset += FingerprintSize;

            byte[] masterKey =
                plain.AsSpan(offset, MasterKeySize).ToArray();

            return new SyncDeviceSecrets(
                userId.ToString(),
                masterKey,
                fingerprint,
                highestSeenRevision);
        }
        catch (CryptographicException exception)
        {
            throw new SyncSecurityException(
                "Windows could not unlock this device's Feather sync key.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);

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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteAtomic(string path, byte[] data)
    {
        string temporaryPath = path + ".tmp";

        using (FileStream stream = new(
                   temporaryPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   4096,
                   FileOptions.WriteThrough))
        {
            stream.Write(data);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }
}

internal sealed class SyncDeviceSecrets : IDisposable
{
    public string UserId { get; }
    public byte[] MasterKey { get; }
    public string KeyBundleFingerprint { get; }
    public long HighestSeenRevision { get; }

    public SyncDeviceSecrets(
        string userId,
        byte[] masterKey,
        string keyBundleFingerprint,
        long highestSeenRevision)
    {
        UserId = userId;
        MasterKey = masterKey;
        KeyBundleFingerprint = keyBundleFingerprint;
        HighestSeenRevision = highestSeenRevision;
    }

    public void Dispose() =>
        CryptographicOperations.ZeroMemory(MasterKey);
}
