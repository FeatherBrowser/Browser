using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace FeatherBrowser.Features.Importing;

internal static class EdgeImporter
{
    private static readonly Regex UrlRegex = new(
        @"https?://[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=%]{3,2048}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static EdgeImportResult Import(BrowserDataStore store)
    {
        var result = new EdgeImportResult();
        string userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Edge", "User Data");

        if (!Directory.Exists(userData))
            throw new DirectoryNotFoundException("Microsoft Edge profile data was not found on this Windows account.");

        var profiles = Directory.EnumerateDirectories(userData)
            .Where(path =>
            {
                string name = Path.GetFileName(path);
                return name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        var sessionUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string profile in profiles)
        {
            result.ProfilesScanned++;
            try
            {
                var bookmarks = ReadBookmarks(profile);
                result.BookmarksImported += store.MergeBookmarks(bookmarks);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{Path.GetFileName(profile)} favorites: {ex.Message}");
            }

            try
            {
                var history = ReadHistory(profile);
                result.HistoryImported += store.MergeHistory(history);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{Path.GetFileName(profile)} history: {ex.Message}");
            }

            try
            {
                foreach (string url in ReadSessionUrls(profile))
                {
                    if (sessionUrls.Add(url) && result.SessionUrls.Count < 40)
                        result.SessionUrls.Add(url);
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{Path.GetFileName(profile)} session tabs: {ex.Message}");
            }
        }

        return result;
    }

    private static List<BrowserBookmark> ReadBookmarks(string profile)
    {
        string path = Path.Combine(profile, "Bookmarks");
        if (!File.Exists(path))
            return [];

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var output = new List<BrowserBookmark>();
        if (!doc.RootElement.TryGetProperty("roots", out JsonElement roots))
            return output;

        foreach (var root in roots.EnumerateObject())
        {
            string rootName = root.Name switch
            {
                "bookmark_bar" => "Favorites bar",
                "other" => "Other favorites",
                "synced" => "Mobile favorites",
                _ => root.Name
            };
            ReadBookmarkNode(root.Value, rootName, output);
        }
        return output;
    }

    private static void ReadBookmarkNode(JsonElement node, string folder, List<BrowserBookmark> output)
    {
        if (node.TryGetProperty("type", out var type) && type.GetString() == "url")
        {
            string url = node.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            string title = node.TryGetProperty("name", out var n) ? n.GetString() ?? url : url;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                output.Add(new BrowserBookmark
                {
                    Title = title,
                    Url = url,
                    Folder = string.IsNullOrWhiteSpace(folder) ? "Imported from Edge" : folder,
                    CreatedAt = DateTimeOffset.Now
                });
            }
            return;
        }

        string currentFolder = folder;
        if (node.TryGetProperty("name", out var nameElement))
        {
            string? nodeName = nameElement.GetString();
            if (!string.IsNullOrWhiteSpace(nodeName) && !string.Equals(nodeName, "Bookmarks bar", StringComparison.OrdinalIgnoreCase))
                currentFolder = string.IsNullOrWhiteSpace(folder) ? nodeName : $"{folder} / {nodeName}";
        }

        if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
            return;

        foreach (var child in children.EnumerateArray())
            ReadBookmarkNode(child, currentFolder, output);
    }

    private static List<HistoryEntry> ReadHistory(string profile)
    {
        string source = Path.Combine(profile, "History");
        if (!File.Exists(source))
            return [];

        string temp = Path.Combine(Path.GetTempPath(), $"feather-edge-history-{Guid.NewGuid():N}.db");
        try
        {
            File.Copy(source, temp, true);
            using var connection = new SqliteConnection($"Data Source={temp};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT url, title, last_visit_time, visit_count
                FROM urls
                WHERE (url LIKE 'http://%' OR url LIKE 'https://%')
                ORDER BY last_visit_time DESC
                LIMIT 5000;
                """;

            using var reader = command.ExecuteReader();
            var output = new List<HistoryEntry>();
            while (reader.Read())
            {
                long chromeTime = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                output.Add(new HistoryEntry
                {
                    Url = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    LastVisited = ChromeTimeToDate(chromeTime),
                    VisitCount = reader.IsDBNull(3) ? 1 : Math.Max(1, reader.GetInt32(3))
                });
            }
            return output;
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static List<string> ReadSessionUrls(string profile)
    {
        var files = new List<string>();
        string sessionsDir = Path.Combine(profile, "Sessions");
        if (Directory.Exists(sessionsDir))
        {
            files.AddRange(Directory.EnumerateFiles(sessionsDir)
                .Where(path =>
                {
                    string name = Path.GetFileName(path);
                    return name.StartsWith("Tabs_", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("Session_", StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(6));
        }

        foreach (string oldName in new[] { "Current Tabs", "Last Tabs", "Current Session", "Last Session" })
        {
            string path = Path.Combine(profile, oldName);
            if (File.Exists(path))
                files.Add(path);
        }

        var output = new List<string>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            byte[] data = File.ReadAllBytes(file);
            ExtractUrls(Encoding.Latin1.GetString(data), output, known);
            ExtractUrls(Encoding.Unicode.GetString(data), output, known);
            if (output.Count >= 40)
                break;
        }

        return output.Take(40).ToList();
    }

    private static void ExtractUrls(string text, List<string> output, HashSet<string> known)
    {
        foreach (Match match in UrlRegex.Matches(text))
        {
            string candidate = match.Value.TrimEnd('.', ',', ';', ':');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                continue;

            string normalized = uri.AbsoluteUri;
            if (known.Add(normalized))
                output.Add(normalized);

            if (output.Count >= 40)
                return;
        }
    }

    private static DateTimeOffset ChromeTimeToDate(long microseconds)
    {
        try
        {
            var epoch = new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return epoch.AddTicks(checked(microseconds * 10));
        }
        catch
        {
            return DateTimeOffset.Now;
        }
    }
}
