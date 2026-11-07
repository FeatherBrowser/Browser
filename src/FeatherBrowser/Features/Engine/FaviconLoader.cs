namespace FeatherBrowser.Features.Engine;

internal static class FaviconLoader
{
    public const int MaxBytes = 512 * 1024;

    public static async Task<byte[]?> LoadAsync(
        IReadOnlyList<string> candidates,
        string pageUrl,
        Func<string, CancellationToken, Task<byte[]?>> fetch,
        CancellationToken token)
    {
        IEnumerable<string> urls = candidates.Count > 0
            ? candidates.OrderBy(static url => url.Contains(".svg", StringComparison.OrdinalIgnoreCase) || url.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase))
            : FallbackFor(pageUrl);

        foreach (string url in urls.Take(4))
        {
            token.ThrowIfCancellationRequested();
            byte[]? bytes = url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                ? DecodeDataUrl(url)
                : await fetch(url, token);
            if (bytes is { Length: > 0 })
                return bytes;
        }

        return null;
    }

    private static IEnumerable<string> FallbackFor(string pageUrl)
    {
        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri? page) && page.Scheme is "http" or "https")
            yield return new Uri(page, "/favicon.ico").AbsoluteUri;
    }

    internal static bool IsImageType(string? mimeType) =>
        string.IsNullOrEmpty(mimeType) ||
        mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
        mimeType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase);

    private static byte[]? DecodeDataUrl(string url)
    {
        int comma = url.IndexOf(',');
        if (comma < 0 || !url[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            byte[] bytes = Convert.FromBase64String(url[(comma + 1)..]);
            return bytes.Length <= MaxBytes ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
