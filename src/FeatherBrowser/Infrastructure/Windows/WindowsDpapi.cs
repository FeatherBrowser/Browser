using System.Security.Cryptography;
using System.Text;

namespace FeatherBrowser.Infrastructure.Windows;

internal static class WindowsDpapi
{
    public static string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] inputBytes = Encoding.UTF8.GetBytes(plaintext);

        try
        {
            byte[] protectedBytes = ProtectedData.Protect(
                inputBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            try
            {
                return Convert.ToBase64String(protectedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(inputBytes);
        }
    }

    public static string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);

        byte[] protectedBytes = Convert.FromBase64String(protectedValue);
        byte[]? clearBytes = null;

        try
        {
            clearBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(clearBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);

            if (clearBytes is not null)
            {
                CryptographicOperations.ZeroMemory(clearBytes);
            }
        }
    }
}