using System.Diagnostics;
using System.IO;

namespace FeatherBrowser.Infrastructure.Persistence;

internal static class SecureFileDelete
{
    public static void Delete(string path, string description)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not delete the {description} file; clearing its contents instead. {ex.Message}");
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            stream.Write(new byte[stream.Length]);
            stream.SetLength(0);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError($"The {description} file could not be deleted or cleared: {ex.Message}");
        }
    }
}
