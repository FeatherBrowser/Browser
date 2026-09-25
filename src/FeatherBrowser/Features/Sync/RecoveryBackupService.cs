using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FeatherBrowser.Features.Sync.Crypto;

namespace FeatherBrowser.Features.Sync;

internal sealed class RecoveryBackupService
{
    private const int Iterations = 600_000;
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public void Save(string path, string recoveryCode, string password)
    {
        ValidatePassword(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] plaintext = Encoding.UTF8.GetBytes(recoveryCode);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] key = DeriveKey(password, salt);

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, BuildAad());

            var file = new RecoveryBackupFile
            {
                Format = 1,
                Kdf = "PBKDF2-HMAC-SHA256",
                Iterations = Iterations,
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                Ciphertext = Convert.ToBase64String(ciphertext)
            };

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(file, JsonOptions), Encoding.UTF8);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public string Load(string path, string password)
    {
        ValidatePassword(password);

        RecoveryBackupFile file = JsonSerializer.Deserialize<RecoveryBackupFile>(File.ReadAllText(path, Encoding.UTF8))
            ?? throw new InvalidDataException("The Feather recovery backup is empty.");

        if (file.Format != 1 ||
            !string.Equals(file.Kdf, "PBKDF2-HMAC-SHA256", StringComparison.Ordinal) ||
            file.Iterations < Iterations)
        {
            throw new InvalidDataException("The Feather recovery backup format is not supported.");
        }

        byte[] salt = Decode(file.Salt, SaltSize, "salt");
        byte[] nonce = Decode(file.Nonce, NonceSize, "nonce");
        byte[] tag = Decode(file.Tag, TagSize, "authentication tag");
        byte[] ciphertext = Convert.FromBase64String(file.Ciphertext);
        byte[] plaintext = new byte[ciphertext.Length];
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, file.Iterations, HashAlgorithmName.SHA256, KeySize);

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, BuildAad());
            string recoveryCode = Encoding.UTF8.GetString(plaintext);
            byte[] validated = RecoveryCode.Parse(recoveryCode);
            CryptographicOperations.ZeroMemory(validated);
            return recoveryCode;
        }
        catch (CryptographicException exception)
        {
            throw new SyncSecurityException("The recovery backup password is incorrect or the backup was modified.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

    private static byte[] BuildAad() => Encoding.UTF8.GetBytes("FeatherBrowser|RecoveryBackup|v1");

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new ArgumentException("The recovery backup password must be at least 12 characters.", nameof(password));
    }

    private static byte[] Decode(string value, int expectedLength, string name)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"The recovery backup {name} is malformed.", exception);
        }

        if (bytes.Length == expectedLength)
            return bytes;

        CryptographicOperations.ZeroMemory(bytes);
        throw new InvalidDataException($"The recovery backup {name} has an invalid length.");
    }

    private sealed class RecoveryBackupFile
    {
        public int Format { get; init; }
        public string Kdf { get; init; } = "";
        public int Iterations { get; init; }
        public string Salt { get; init; } = "";
        public string Nonce { get; init; } = "";
        public string Tag { get; init; } = "";
        public string Ciphertext { get; init; } = "";
    }
}
