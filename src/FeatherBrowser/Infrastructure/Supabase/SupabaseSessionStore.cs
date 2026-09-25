using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FeatherBrowser.Infrastructure.Supabase;

internal sealed class SupabaseSessionStore
{
    private readonly string _path;

    public SupabaseSessionStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FeatherBrowser",
            "Data");

        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "account-session.bin");
    }

    public void Save(SupabaseSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(session);
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

    public SupabaseSession? Load()
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

            return JsonSerializer.Deserialize<SupabaseSession>(plain);
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
