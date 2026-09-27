using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace FeatherBrowser.Infrastructure.Resources;

internal static class EmbeddedAssets
{
    private static readonly ConcurrentDictionary<string, string> Cache =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, byte[]> ByteCache =
        new(StringComparer.Ordinal);

    public static string Load(string name) =>
        Cache.GetOrAdd(name, static key =>
        {
            string resourceName = $"FeatherBrowser.Assets.{key}";

            using Stream stream =
                typeof(EmbeddedAssets).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded browser asset was not found: {resourceName}");

            using var reader = new StreamReader(stream, Encoding.UTF8);

            return reader.ReadToEnd();
        });

    public static byte[] LoadBytes(string name) =>
        ByteCache.GetOrAdd(name, static key =>
        {
            string resourceName = $"FeatherBrowser.Assets.{key}";

            using Stream stream =
                typeof(EmbeddedAssets).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded browser asset was not found: {resourceName}");

            using var memory = new MemoryStream();

            stream.CopyTo(memory);

            return memory.ToArray();
        });

    public static string LoadPage(string name) =>
        Load($"{name}.html")
            .Replace(
                "__APP_VERSION__",
                AppInfo.Version,
                StringComparison.Ordinal)
            .Replace(
                "__PAGE_STYLES__",
                Load($"{name}.css") + "\n" + Load("glass.css"),
                StringComparison.Ordinal)
            .Replace(
                "__PAGE_SCRIPT__",
                Load($"{name}.js"),
                StringComparison.Ordinal);
}