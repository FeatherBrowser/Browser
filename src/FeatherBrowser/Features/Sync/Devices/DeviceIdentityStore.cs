using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FeatherBrowser.Features.Sync.Devices;

internal sealed class DeviceIdentityStore
{
    private readonly string _path;

    public DeviceIdentityStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "FeatherBrowser",
            "Data");

        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "sync-device-identity.bin");
    }

    public DeviceIdentity LoadOrCreate(string deviceName)
    {
        DeviceIdentity? existing = Load();

        if (existing is not null)
            return existing;

        using ECDiffieHellman ecdh =
            ECDiffieHellman.Create(
                ECCurve.NamedCurves.nistP256);

        byte[] privateKey = ecdh.ExportPkcs8PrivateKey();
        byte[] publicKey = ecdh.ExportSubjectPublicKeyInfo();

        var identity = new DeviceIdentity(
            Guid.NewGuid(),
            NormalizeName(deviceName),
            privateKey,
            Convert.ToBase64String(publicKey));

        CryptographicOperations.ZeroMemory(publicKey);

        Save(identity);

        return identity;
    }

    public DeviceIdentity? Load()
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

            DeviceIdentityFile? file =
                JsonSerializer.Deserialize<DeviceIdentityFile>(plain);

            if (file is null ||
                !Guid.TryParse(file.DeviceId, out Guid deviceId) ||
                string.IsNullOrWhiteSpace(file.PrivateKey) ||
                string.IsNullOrWhiteSpace(file.PublicKey))
            {
                throw new SyncSecurityException(
                    "The local Feather device identity is invalid.");
            }

            byte[] privateKey =
                Convert.FromBase64String(file.PrivateKey);

            ValidateKeyPair(privateKey, file.PublicKey);

            return new DeviceIdentity(
                deviceId,
                NormalizeName(file.DeviceName),
                privateKey,
                file.PublicKey);
        }
        catch (CryptographicException exception)
        {
            throw new SyncSecurityException(
                "Windows could not unlock this device's Feather identity.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new SyncSecurityException(
                "The local Feather device identity is corrupted.",
                exception);
        }
        catch (FormatException exception)
        {
            throw new SyncSecurityException(
                "The local Feather device identity is malformed.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);

            if (plain is not null)
                CryptographicOperations.ZeroMemory(plain);
        }
    }

    private void Save(DeviceIdentity identity)
    {
        var file = new DeviceIdentityFile
        {
            DeviceId = identity.DeviceId.ToString(),
            DeviceName = identity.DeviceName,
            PrivateKey = Convert.ToBase64String(identity.PrivateKey),
            PublicKey = identity.PublicKey
        };

        byte[] plain =
            JsonSerializer.SerializeToUtf8Bytes(file);

        byte[]? protectedBytes = null;

        try
        {
            protectedBytes = ProtectedData.Protect(
                plain,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            WriteAtomic(_path, protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);

            if (protectedBytes is not null)
                CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private static void ValidateKeyPair(
        byte[] privateKey,
        string publicKeyBase64)
    {
        byte[] publicKey =
            Convert.FromBase64String(publicKeyBase64);

        try
        {
            using ECDiffieHellman privateEcdh =
                ECDiffieHellman.Create();

            privateEcdh.ImportPkcs8PrivateKey(
                privateKey,
                out int privateRead);

            if (privateRead != privateKey.Length)
                throw new SyncSecurityException(
                    "The local Feather device private key is invalid.");

            using ECDiffieHellman publicEcdh =
                ECDiffieHellman.Create();

            publicEcdh.ImportSubjectPublicKeyInfo(
                publicKey,
                out int publicRead);

            if (publicRead != publicKey.Length)
                throw new SyncSecurityException(
                    "The local Feather device public key is invalid.");

            byte[] derivedPublic =
                privateEcdh.ExportSubjectPublicKeyInfo();

            try
            {
                if (!CryptographicOperations.FixedTimeEquals(
                        derivedPublic,
                        publicKey))
                {
                    throw new SyncSecurityException(
                        "The local Feather device key pair does not match.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(derivedPublic);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
        }
    }

    private static string NormalizeName(string value)
    {
        string name = string.IsNullOrWhiteSpace(value)
            ? Environment.MachineName
            : value.Trim();

        return name.Length <= 80
            ? name
            : name[..80];
    }

    private static void WriteAtomic(
        string path,
        byte[] data)
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

        File.Move(
            temporaryPath,
            path,
            overwrite: true);
    }

    private sealed class DeviceIdentityFile
    {
        public string DeviceId { get; init; } = "";
        public string DeviceName { get; init; } = "";
        public string PrivateKey { get; init; } = "";
        public string PublicKey { get; init; } = "";
    }
}

internal sealed class DeviceIdentity : IDisposable
{
    public Guid DeviceId { get; }
    public string DeviceName { get; }
    public byte[] PrivateKey { get; }
    public string PublicKey { get; }

    public DeviceIdentity(
        Guid deviceId,
        string deviceName,
        byte[] privateKey,
        string publicKey)
    {
        DeviceId = deviceId;
        DeviceName = deviceName;
        PrivateKey = privateKey;
        PublicKey = publicKey;
    }

    public void Dispose() =>
        CryptographicOperations.ZeroMemory(PrivateKey);
}
