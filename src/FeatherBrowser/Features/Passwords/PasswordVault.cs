using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Infrastructure.Windows;

namespace FeatherBrowser.Features.Passwords;

internal sealed class PasswordVault
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private List<SavedCredential> _credentials;

    public int Count
    {
        get { lock (_sync) return _credentials.Count; }
    }

    public PasswordVault()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FeatherBrowser",
            "Data");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "passwords.json");
        _credentials = Load();
    }

    public PasswordImportResult ImportEdgeCsv(string csvPath)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException("The selected password CSV file was not found.", csvPath);

        string text = File.ReadAllText(csvPath, Encoding.UTF8);
        List<List<string>> rows = ParseCsv(text);
        if (rows.Count < 2)
            throw new InvalidDataException("The CSV did not contain any password rows.");

        Dictionary<string, int> header = rows[0]
            .Select((value, index) => (value: value.Trim(), index))
            .Where(x => !string.IsNullOrWhiteSpace(x.value))
            .GroupBy(x => x.value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().index, StringComparer.OrdinalIgnoreCase);

        int urlIndex = FindHeader(header, "url", "origin", "website");
        int usernameIndex = FindHeader(header, "username", "user name", "login");
        int passwordIndex = FindHeader(header, "password");
        int nameIndex = FindHeader(header, "name", "title");

        if (urlIndex < 0 || usernameIndex < 0 || passwordIndex < 0)
            throw new InvalidDataException("This does not look like an Edge/Chromium password CSV. Expected url, username and password columns.");

        var result = new PasswordImportResult();

        lock (_sync)
        {
            foreach (List<string> row in rows.Skip(1))
            {
                string url = Cell(row, urlIndex);
                string username = Cell(row, usernameIndex);
                string password = Cell(row, passwordIndex);
                string name = nameIndex >= 0 ? Cell(row, nameIndex) : "";

                if (string.IsNullOrWhiteSpace(password) || !TryNormalizeOrigin(url, out string origin))
                {
                    result.Skipped++;
                    continue;
                }

                string protectedPassword = WindowsDpapi.Protect(password);
                SavedCredential? existing = _credentials.FirstOrDefault(x =>
                    string.Equals(x.Origin, origin, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Username, username, StringComparison.Ordinal));

                if (existing is null)
                {
                    _credentials.Add(new SavedCredential
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? origin : name,
                        Origin = origin,
                        Username = username,
                        ProtectedPassword = protectedPassword,
                        ImportedAt = DateTimeOffset.Now
                    });
                    result.Imported++;
                }
                else
                {
                    existing.Name = string.IsNullOrWhiteSpace(name) ? existing.Name : name;
                    existing.ProtectedPassword = protectedPassword;
                    existing.ImportedAt = DateTimeOffset.Now;
                    result.Updated++;
                }
            }

            _credentials = _credentials
                .OrderBy(x => x.Origin, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Save();
        }

        return result;
    }

    public List<SavedCredential> FindForUrl(string? url)
    {
        if (!TryNormalizeOrigin(url, out string origin))
            return [];

        lock (_sync)
        {
            return _credentials
                .Where(x => string.Equals(x.Origin, origin, StringComparison.OrdinalIgnoreCase))
                .Select(Clone)
                .ToList();
        }
    }

    public string RevealPassword(SavedCredential credential)
        => WindowsDpapi.Unprotect(credential.ProtectedPassword);

    public void Clear()
    {
        lock (_sync)
        {
            _credentials.Clear();
            Save();
        }
    }

    private List<SavedCredential> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return [];
            return JsonSerializer.Deserialize<List<SavedCredential>>(File.ReadAllText(_path), _jsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Save()
    {
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_credentials, _jsonOptions));
        File.Move(temp, _path, true);
    }

    private static SavedCredential Clone(SavedCredential value) => new()
    {
        Name = value.Name,
        Origin = value.Origin,
        Username = value.Username,
        ProtectedPassword = value.ProtectedPassword,
        ImportedAt = value.ImportedAt
    };

    private static int FindHeader(Dictionary<string, int> header, params string[] names)
    {
        foreach (string name in names)
            if (header.TryGetValue(name, out int index))
                return index;
        return -1;
    }

    private static string Cell(List<string> row, int index)
        => index >= 0 && index < row.Count ? row[index] : "";

    private static bool TryNormalizeOrigin(string? value, out string origin)
    {
        origin = "";
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;

        origin = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        return !string.IsNullOrWhiteSpace(origin);
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(ch);
                }
                continue;
            }

            if (ch == '"' && cell.Length == 0)
            {
                quoted = true;
            }
            else if (ch == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;

                row.Add(cell.ToString());
                cell.Clear();
                if (row.Any(x => x.Length > 0))
                    rows.Add(row);
                row = new List<string>();
            }
            else
            {
                cell.Append(ch);
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            if (row.Any(x => x.Length > 0))
                rows.Add(row);
        }

        return rows;
    }
}
