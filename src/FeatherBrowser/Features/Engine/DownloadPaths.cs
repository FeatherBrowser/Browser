using System.IO;

namespace FeatherBrowser.Features.Engine;

internal static class DownloadPaths
{
    public static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public static string Unique(string folder, string? suggestedName)
    {
        string name = Sanitize(suggestedName);
        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        string candidate = Path.Combine(folder, name);

        for (int copy = 2; File.Exists(candidate) || Directory.Exists(candidate); copy++)
            candidate = Path.Combine(folder, $"{stem} ({copy}){extension}");

        return candidate;
    }

    public static string Sanitize(string? suggestedName)
    {
        string name = Path.GetFileName(suggestedName ?? string.Empty).Trim().TrimEnd('.');
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        if (name.Length == 0)
            name = "download";
        if (name.Length > 180)
            name = name[..120] + Path.GetExtension(name);

        string stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3])))
            name = "_" + name;

        return name;
    }
}
