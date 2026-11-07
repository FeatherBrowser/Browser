using System.Security.Cryptography;
using System.Text;
using FeatherBrowser.Features.Sync.Models;

namespace FeatherBrowser.Features.Sync.Crypto;

internal static class SyncCrypto
{
    private const int KeySize = 32;
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly byte[] WrapInfo =
        Encoding.UTF8.GetBytes("FeatherBrowser/SyncMasterKey/v1");

    private static readonly byte[] DataInfo =
        Encoding.UTF8.GetBytes("FeatherBrowser/SyncData/v1");

    public static byte[] GenerateMasterKey() =>
        RandomNumberGenerator.GetBytes(KeySize);

    public static SyncKeyBundle CreateKeyBundle(
        string userId,
        ReadOnlySpan<byte> masterKey,
        ReadOnlySpan<byte> recoveryKey)
    {
        ValidateKey(masterKey, nameof(masterKey));
        ValidateKey(recoveryKey, nameof(recoveryKey));
        ValidateUserId(userId);

        byte[] recoverySalt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] dataSalt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] wrapped = new byte[KeySize];
        byte[] wrappingKey = HkdfSha256(recoveryKey, recoverySalt, WrapInfo);
        byte[] aad = BuildKeyBundleAad(userId);

        try
        {
            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Encrypt(nonce, masterKey, wrapped, tag, aad);

            return new SyncKeyBundle
            {
                Version = 1,
                Cipher = "AES-256-GCM",
                Kdf = "HKDF-SHA256",
                RecoverySalt = Convert.ToBase64String(recoverySalt),
                DataSalt = Convert.ToBase64String(dataSalt),
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                WrappedMasterKey = Convert.ToBase64String(wrapped)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(aad);
            CryptographicOperations.ZeroMemory(wrapped);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(dataSalt);
            CryptographicOperations.ZeroMemory(recoverySalt);
        }
    }

    public static byte[] UnwrapMasterKey(
        string userId,
        SyncKeyBundle bundle,
        ReadOnlySpan<byte> recoveryKey)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ValidateUserId(userId);
        ValidateKey(recoveryKey, nameof(recoveryKey));
        ValidateBundle(bundle);

        byte[] recoverySalt = DecodeExact(bundle.RecoverySalt, SaltSize, "recovery salt");
        byte[] nonce = DecodeExact(bundle.Nonce, NonceSize, "key nonce");
        byte[] tag = DecodeExact(bundle.Tag, TagSize, "key tag");
        byte[] wrapped = DecodeExact(bundle.WrappedMasterKey, KeySize, "wrapped master key");
        byte[] wrappingKey = HkdfSha256(recoveryKey, recoverySalt, WrapInfo);
        byte[] aad = BuildKeyBundleAad(userId);
        byte[] masterKey = new byte[KeySize];

        try
        {
            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Decrypt(nonce, wrapped, tag, masterKey, aad);
            return masterKey;
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw new SyncSecurityException(
                "The recovery key could not decrypt this Feather account. The key may be wrong or the server record may have been modified.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(aad);
            CryptographicOperations.ZeroMemory(wrapped);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(recoverySalt);
        }
    }

    public static EncryptedSyncBlob Encrypt(
        string userId,
        long revision,
        SyncKeyBundle bundle,
        ReadOnlySpan<byte> masterKey,
        ReadOnlySpan<byte> plaintext)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ValidateUserId(userId);
        ValidateKey(masterKey, nameof(masterKey));

        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision));

        ValidateBundle(bundle);

        byte[] dataSalt = DecodeExact(bundle.DataSalt, SaltSize, "data salt");
        byte[] dataKey = HkdfSha256(masterKey, dataSalt, DataInfo);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] aad = BuildDataAad(userId, revision);

        try
        {
            using var aes = new AesGcm(dataKey, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

            return new EncryptedSyncBlob
            {
                Version = 1,
                Cipher = "AES-256-GCM",
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                Ciphertext = Convert.ToBase64String(ciphertext)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aad);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(dataSalt);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public static byte[] Decrypt(
        string userId,
        long revision,
        SyncKeyBundle bundle,
        ReadOnlySpan<byte> masterKey,
        EncryptedSyncBlob encryptedBlob)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(encryptedBlob);
        ValidateUserId(userId);
        ValidateKey(masterKey, nameof(masterKey));

        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision));

        ValidateBundle(bundle);
        ValidateBlob(encryptedBlob);

        byte[] dataSalt = DecodeExact(bundle.DataSalt, SaltSize, "data salt");
        byte[] dataKey = HkdfSha256(masterKey, dataSalt, DataInfo);
        byte[] nonce = DecodeExact(encryptedBlob.Nonce, NonceSize, "data nonce");
        byte[] tag = DecodeExact(encryptedBlob.Tag, TagSize, "data tag");
        byte[] ciphertext;

        try
        {
            ciphertext = Convert.FromBase64String(encryptedBlob.Ciphertext);
        }
        catch (FormatException exception)
        {
            throw new SyncSecurityException("The encrypted sync payload is malformed.", exception);
        }

        byte[] plaintext = new byte[ciphertext.Length];
        byte[] aad = BuildDataAad(userId, revision);

        try
        {
            using var aes = new AesGcm(dataKey, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            return plaintext;
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new SyncSecurityException(
                "Feather could not authenticate the encrypted sync payload. The data may be corrupted, stale, or modified.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aad);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(dataSalt);
        }
    }

    public static string Fingerprint(SyncKeyBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ValidateBundle(bundle);

        byte[] recoverySalt = DecodeExact(bundle.RecoverySalt, SaltSize, "recovery salt");
        byte[] dataSalt = DecodeExact(bundle.DataSalt, SaltSize, "data salt");
        byte[] nonce = DecodeExact(bundle.Nonce, NonceSize, "key nonce");
        byte[] tag = DecodeExact(bundle.Tag, TagSize, "key tag");
        byte[] wrapped = DecodeExact(bundle.WrappedMasterKey, KeySize, "wrapped master key");

        byte[] metadata = Encoding.UTF8.GetBytes(
            $"{bundle.Version}\n{bundle.Cipher}\n{bundle.Kdf}\n");

        byte[] canonical = new byte[
            metadata.Length +
            recoverySalt.Length +
            dataSalt.Length +
            nonce.Length +
            tag.Length +
            wrapped.Length];

        int offset = 0;
        metadata.CopyTo(canonical, offset);
        offset += metadata.Length;
        recoverySalt.CopyTo(canonical, offset);
        offset += recoverySalt.Length;
        dataSalt.CopyTo(canonical, offset);
        offset += dataSalt.Length;
        nonce.CopyTo(canonical, offset);
        offset += nonce.Length;
        tag.CopyTo(canonical, offset);
        offset += tag.Length;
        wrapped.CopyTo(canonical, offset);

        try
        {
            return Convert.ToHexString(SHA256.HashData(canonical));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
            CryptographicOperations.ZeroMemory(metadata);
            CryptographicOperations.ZeroMemory(wrapped);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(dataSalt);
            CryptographicOperations.ZeroMemory(recoverySalt);
        }
    }

    private static byte[] HkdfSha256(
        ReadOnlySpan<byte> inputKeyMaterial,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info)
    {
        byte[] saltBytes = salt.ToArray();
        byte[] ikmBytes = inputKeyMaterial.ToArray();
        byte[] infoBytes = info.ToArray();
        byte[] prk;
        byte[] expandInput = new byte[infoBytes.Length + 1];

        try
        {
            using (var extract = new HMACSHA256(saltBytes))
                prk = extract.ComputeHash(ikmBytes);

            infoBytes.CopyTo(expandInput, 0);
            expandInput[^1] = 0x01;

            try
            {
                using var expand = new HMACSHA256(prk);
                return expand.ComputeHash(expandInput);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(prk);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expandInput);
            CryptographicOperations.ZeroMemory(infoBytes);
            CryptographicOperations.ZeroMemory(ikmBytes);
            CryptographicOperations.ZeroMemory(saltBytes);
        }
    }

    private static byte[] BuildKeyBundleAad(string userId) =>
        Encoding.UTF8.GetBytes($"FeatherBrowser|sync-key|v1|{userId}");

    private static byte[] BuildDataAad(string userId, long revision) =>
        Encoding.UTF8.GetBytes($"FeatherBrowser|sync-data|v1|{userId}|{revision}");

    private static void ValidateBundle(SyncKeyBundle bundle)
    {
        if (bundle.Version != 1 ||
            !string.Equals(bundle.Cipher, "AES-256-GCM", StringComparison.Ordinal) ||
            !string.Equals(bundle.Kdf, "HKDF-SHA256", StringComparison.Ordinal))
        {
            throw new SyncSecurityException("This Feather sync key bundle uses an unsupported cryptographic format.");
        }
    }

    private static void ValidateBlob(EncryptedSyncBlob blob)
    {
        if (blob.Version != 1 ||
            !string.Equals(blob.Cipher, "AES-256-GCM", StringComparison.Ordinal))
        {
            throw new SyncSecurityException("This Feather sync payload uses an unsupported cryptographic format.");
        }
    }

    private static byte[] DecodeExact(string value, int length, string field)
    {
        try
        {
            byte[] decoded = Convert.FromBase64String(value);

            if (decoded.Length == length)
                return decoded;

            CryptographicOperations.ZeroMemory(decoded);
            throw new SyncSecurityException($"The sync {field} has an invalid length.");
        }
        catch (FormatException exception)
        {
            throw new SyncSecurityException($"The sync {field} is malformed.", exception);
        }
    }

    private static void ValidateKey(ReadOnlySpan<byte> key, string parameterName)
    {
        if (key.Length != KeySize)
            throw new ArgumentException("A 256-bit key is required.", parameterName);
    }

    private static void ValidateUserId(string userId)
    {
        if (!Guid.TryParse(userId, out _))
            throw new SyncSecurityException("The Supabase user id is invalid.");
    }
}
