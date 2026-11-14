using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace FeatherBrowser.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
internal static class WindowsDpapi
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    private const DataProtectionScope Scope =
        DataProtectionScope.CurrentUser;

    public static string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] clearBytes = Utf8.GetBytes(plaintext);
        byte[]? encryptedBytes = null;

        try
        {
            encryptedBytes = ProtectedData.Protect(
                clearBytes,
                optionalEntropy: null,
                scope: Scope
            );

            return Convert.ToBase64String(encryptedBytes);
        }
        finally
        {
            ZeroMemory(clearBytes);
            ZeroMemory(encryptedBytes);
        }
    }

    public static string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);

        byte[] encryptedBytes =
            Convert.FromBase64String(protectedValue);

        byte[]? clearBytes = null;

        try
        {
            clearBytes = ProtectedData.Unprotect(
                encryptedBytes,
                optionalEntropy: null,
                scope: Scope
            );

            return Utf8.GetString(clearBytes);
        }
        finally
        {
            ZeroMemory(encryptedBytes);
            ZeroMemory(clearBytes);
        }
    }

    private static void ZeroMemory(byte[]? data)
    {
        if (data is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }
}
