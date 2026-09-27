using System.Security.Cryptography;

namespace FeatherBrowser.Features.Sync.Crypto;

internal static class RecoveryCode
{
    private const string Prefix = "FEATHER1-";
    private const int KeySize = 32;

    public static (string Code, byte[] Key) Create()
    {
        byte[] key = RandomNumberGenerator.GetBytes(KeySize);
        string hex = Convert.ToHexString(key);

        string grouped = string.Join(
            "-",
            Enumerable.Range(0, hex.Length / 8)
                .Select(i => hex.Substring(i * 8, 8)));

        return ($"{Prefix}{grouped}", key);
    }

    public static byte[] Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new SyncSecurityException("The Feather recovery key is empty.");

        string normalized = value.Trim();

        if (!normalized.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            throw new SyncSecurityException("This is not a Feather v1 recovery key.");

        normalized = normalized[Prefix.Length..]
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);

        if (normalized.Length != KeySize * 2)
            throw new SyncSecurityException("The Feather recovery key has an invalid length.");

        try
        {
            byte[] key = Convert.FromHexString(normalized);

            if (key.Length != KeySize)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new SyncSecurityException("The Feather recovery key has an invalid length.");
            }

            return key;
        }
        catch (FormatException exception)
        {
            throw new SyncSecurityException("The Feather recovery key contains invalid characters.", exception);
        }
    }
}
