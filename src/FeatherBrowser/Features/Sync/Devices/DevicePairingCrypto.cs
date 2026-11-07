using System.Security.Cryptography;
using System.Text;

namespace FeatherBrowser.Features.Sync.Devices;

internal static class DevicePairingCrypto
{
    private const int KeySize = 32;
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static string PairingCode(string publicKeyBase64)
    {
        byte[] publicKey =
            DecodePublicKey(publicKeyBase64);

        try
        {
            byte[] hash = SHA256.HashData(publicKey);

            try
            {
                string hex =
                    Convert.ToHexString(hash.AsSpan(0, 8));

                return string.Join(
                    "-",
                    Enumerable.Range(0, 4)
                        .Select(index =>
                            hex.Substring(index * 4, 4)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
        }
    }

    public static DeviceKeyTransfer EncryptMasterKey(
        string userId,
        DeviceIdentity source,
        RemoteDevice target,
        string keyBundleFingerprint,
        ReadOnlySpan<byte> masterKey)
    {
        ValidateUserId(userId);

        if (masterKey.Length != KeySize)
            throw new ArgumentException(
                "A 256-bit master key is required.",
                nameof(masterKey));

        byte[] targetPublic =
            DecodePublicKey(target.PublicKey);

        byte[] salt =
            RandomNumberGenerator.GetBytes(SaltSize);

        byte[] nonce =
            RandomNumberGenerator.GetBytes(NonceSize);

        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[KeySize];
        byte[]? sharedSecret = null;
        byte[]? transferKey = null;
        byte[]? aad = null;

        try
        {
            using ECDiffieHellman sourceEcdh =
                ECDiffieHellman.Create();

            sourceEcdh.ImportPkcs8PrivateKey(
                source.PrivateKey,
                out int privateRead);

            if (privateRead != source.PrivateKey.Length)
                throw new SyncSecurityException(
                    "The local device private key is invalid.");

            using ECDiffieHellman targetEcdh =
                ECDiffieHellman.Create();

            targetEcdh.ImportSubjectPublicKeyInfo(
                targetPublic,
                out int publicRead);

            if (publicRead != targetPublic.Length)
                throw new SyncSecurityException(
                    "The target device public key is invalid.");

            sharedSecret =
                sourceEcdh.DeriveKeyMaterial(
                    targetEcdh.PublicKey);

            transferKey = DeriveTransferKey(
                sharedSecret,
                salt,
                userId,
                source.DeviceId,
                target.DeviceId);

            aad = BuildAad(
                userId,
                source.DeviceId,
                target.DeviceId,
                keyBundleFingerprint);

            using var aes =
                new AesGcm(transferKey, TagSize);

            aes.Encrypt(
                nonce,
                masterKey,
                ciphertext,
                tag,
                aad);

            return new DeviceKeyTransfer
            {
                SourceDeviceId =
                    source.DeviceId.ToString(),
                TargetDeviceId =
                    target.DeviceId.ToString(),
                SourcePublicKey =
                    source.PublicKey,
                KeyBundleFingerprint =
                    keyBundleFingerprint,
                Salt =
                    Convert.ToBase64String(salt),
                Nonce =
                    Convert.ToBase64String(nonce),
                Tag =
                    Convert.ToBase64String(tag),
                EncryptedMasterKey =
                    Convert.ToBase64String(ciphertext)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(targetPublic);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);

            if (sharedSecret is not null)
                CryptographicOperations.ZeroMemory(sharedSecret);

            if (transferKey is not null)
                CryptographicOperations.ZeroMemory(transferKey);

            if (aad is not null)
                CryptographicOperations.ZeroMemory(aad);
        }
    }

    public static byte[] DecryptMasterKey(
        string userId,
        DeviceIdentity target,
        DeviceKeyTransfer transfer)
    {
        ValidateUserId(userId);

        if (!Guid.TryParse(
                transfer.SourceDeviceId,
                out Guid sourceDeviceId) ||
            !Guid.TryParse(
                transfer.TargetDeviceId,
                out Guid targetDeviceId))
        {
            throw new SyncSecurityException(
                "The device transfer has invalid device ids.");
        }

        if (target.DeviceId != targetDeviceId)
            throw new SyncSecurityException(
                "This device transfer is intended for another device.");

        byte[] sourcePublic =
            DecodePublicKey(transfer.SourcePublicKey);

        byte[] salt =
            DecodeExact(
                transfer.Salt,
                SaltSize,
                "pairing salt");

        byte[] nonce =
            DecodeExact(
                transfer.Nonce,
                NonceSize,
                "pairing nonce");

        byte[] tag =
            DecodeExact(
                transfer.Tag,
                TagSize,
                "pairing tag");

        byte[] ciphertext =
            DecodeExact(
                transfer.EncryptedMasterKey,
                KeySize,
                "encrypted master key");

        byte[]? sharedSecret = null;
        byte[]? transferKey = null;
        byte[]? aad = null;
        byte[] masterKey = new byte[KeySize];

        try
        {
            using ECDiffieHellman targetEcdh =
                ECDiffieHellman.Create();

            targetEcdh.ImportPkcs8PrivateKey(
                target.PrivateKey,
                out int privateRead);

            if (privateRead != target.PrivateKey.Length)
                throw new SyncSecurityException(
                    "The local device private key is invalid.");

            using ECDiffieHellman sourceEcdh =
                ECDiffieHellman.Create();

            sourceEcdh.ImportSubjectPublicKeyInfo(
                sourcePublic,
                out int publicRead);

            if (publicRead != sourcePublic.Length)
                throw new SyncSecurityException(
                    "The approving device public key is invalid.");

            sharedSecret =
                targetEcdh.DeriveKeyMaterial(
                    sourceEcdh.PublicKey);

            transferKey = DeriveTransferKey(
                sharedSecret,
                salt,
                userId,
                sourceDeviceId,
                targetDeviceId);

            aad = BuildAad(
                userId,
                sourceDeviceId,
                targetDeviceId,
                transfer.KeyBundleFingerprint);

            using var aes =
                new AesGcm(transferKey, TagSize);

            aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                masterKey,
                aad);

            return masterKey;
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(masterKey);

            throw new SyncSecurityException(
                "The device approval could not be authenticated. Do not trust this transfer.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sourcePublic);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);

            if (sharedSecret is not null)
                CryptographicOperations.ZeroMemory(sharedSecret);

            if (transferKey is not null)
                CryptographicOperations.ZeroMemory(transferKey);

            if (aad is not null)
                CryptographicOperations.ZeroMemory(aad);
        }
    }

    private static byte[] DeriveTransferKey(
        ReadOnlySpan<byte> sharedSecret,
        ReadOnlySpan<byte> salt,
        string userId,
        Guid sourceDeviceId,
        Guid targetDeviceId)
    {
        byte[] info = Encoding.UTF8.GetBytes(
            $"FeatherBrowser/DeviceApproval/v1/{userId}/{sourceDeviceId:D}/{targetDeviceId:D}");

        try
        {
            return HkdfSha256(
                sharedSecret,
                salt,
                info);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(info);
        }
    }

    private static byte[] BuildAad(
        string userId,
        Guid sourceDeviceId,
        Guid targetDeviceId,
        string keyBundleFingerprint) =>
        Encoding.UTF8.GetBytes(
            $"FeatherBrowser|device-approval|v1|{userId}|{sourceDeviceId:D}|{targetDeviceId:D}|{keyBundleFingerprint}");

    private static byte[] HkdfSha256(
        ReadOnlySpan<byte> inputKeyMaterial,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info)
    {
        byte[] saltBytes = salt.ToArray();
        byte[] ikmBytes = inputKeyMaterial.ToArray();
        byte[] infoBytes = info.ToArray();
        byte[] expandInput =
            new byte[infoBytes.Length + 1];

        try
        {
            byte[] prk;

            using (var extract =
                   new HMACSHA256(saltBytes))
            {
                prk = extract.ComputeHash(ikmBytes);
            }

            try
            {
                infoBytes.CopyTo(
                    expandInput,
                    0);

                expandInput[^1] = 0x01;

                using var expand =
                    new HMACSHA256(prk);

                return expand.ComputeHash(
                    expandInput);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(prk);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(saltBytes);
            CryptographicOperations.ZeroMemory(ikmBytes);
            CryptographicOperations.ZeroMemory(infoBytes);
            CryptographicOperations.ZeroMemory(expandInput);
        }
    }

    private static byte[] DecodePublicKey(
        string value)
    {
        byte[] publicKey;

        try
        {
            publicKey =
                Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new SyncSecurityException(
                "A device public key is malformed.",
                exception);
        }

        try
        {
            using ECDiffieHellman ecdh =
                ECDiffieHellman.Create();

            ecdh.ImportSubjectPublicKeyInfo(
                publicKey,
                out int read);

            if (read != publicKey.Length)
                throw new SyncSecurityException(
                    "A device public key contains trailing data.");

            return publicKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(publicKey);
            throw;
        }
    }

    private static byte[] DecodeExact(
        string value,
        int expectedLength,
        string field)
    {
        try
        {
            byte[] decoded =
                Convert.FromBase64String(value);

            if (decoded.Length == expectedLength)
                return decoded;

            CryptographicOperations.ZeroMemory(decoded);

            throw new SyncSecurityException(
                $"The {field} has an invalid length.");
        }
        catch (FormatException exception)
        {
            throw new SyncSecurityException(
                $"The {field} is malformed.",
                exception);
        }
    }

    private static void ValidateUserId(
        string userId)
    {
        if (!Guid.TryParse(userId, out _))
            throw new SyncSecurityException(
                "The Supabase user id is invalid.");
    }
}

internal sealed class RemoteDevice
{
    public required Guid DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public required string PublicKey { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }

    public string PairingCode =>
        DevicePairingCrypto.PairingCode(
            PublicKey);
}

internal sealed class DeviceKeyTransfer
{
    public required string SourceDeviceId { get; init; }
    public required string TargetDeviceId { get; init; }
    public required string SourcePublicKey { get; init; }
    public required string KeyBundleFingerprint { get; init; }
    public required string Salt { get; init; }
    public required string Nonce { get; init; }
    public required string Tag { get; init; }
    public required string EncryptedMasterKey { get; init; }
}
