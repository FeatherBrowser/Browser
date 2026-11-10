using System.IO;
using FeatherBrowser.Features.Engine;

namespace FeatherBrowser.Tests;

public sealed class EngineTests
{
    [Theory]
    [InlineData("--type=renderer --lang=en-US", "Renderer")]
    [InlineData("--type=gpu-process --gpu-preferences=x", "GPU")]
    [InlineData("\"C:\\Feather\\FeatherBrowser.exe\" --type=utility --utility-sub-type=network.mojom.NetworkService --lang=en", "Network service")]
    [InlineData("--type=utility --utility-sub-type=storage.mojom.StorageService", "Storage service")]
    [InlineData("--type=utility --utility-sub-type=foo.mojom.Bar", "Utility · foo")]
    [InlineData("--type=crashpad-handler", "Crash reporter")]
    [InlineData("", "Chromium helper")]
    [InlineData(null, "Chromium helper")]
    public void ChromiumProcessRolesAreReadable(string? commandLine, string expected)
    {
        Assert.Equal(expected, EngineProcesses.Describe(commandLine));
    }

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("", "download")]
    [InlineData(null, "download")]
    [InlineData("trailing.dots...", "trailing.dots")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("com1", "_com1")]
    public void DownloadNamesAreSafe(string? suggested, string expected)
    {
        Assert.Equal(expected, DownloadPaths.Sanitize(suggested));
    }

    [Fact]
    public void DownloadsNeverOverwriteExistingFiles()
    {
        string folder = Path.Join(Path.GetTempPath(), "feather-download-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Join(folder, "report.pdf"), "x");
            File.WriteAllText(Path.Join(folder, "report (2).pdf"), "x");

            Assert.Equal(Path.Join(folder, "report (3).pdf"), DownloadPaths.Unique(folder, "report.pdf"));
            Assert.Equal(Path.Join(folder, "notes.txt"), DownloadPaths.Unique(folder, "notes.txt"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void LongDownloadNamesKeepTheirExtension()
    {
        string name = DownloadPaths.Sanitize(new string('a', 300) + ".zip");
        Assert.True(name.Length <= 180);
        Assert.EndsWith(".zip", name);
    }

    [Fact]
    public async Task FaviconsFallBackToSiteIconAndPreferRasterImages()
    {
        var fetched = new List<string>();
        Task<byte[]?> Fetch(string url, CancellationToken _)
        {
            fetched.Add(url);
            return Task.FromResult<byte[]?>(url.EndsWith(".png", StringComparison.Ordinal) ? [1, 2, 3] : null);
        }

        Assert.Null(await FaviconLoader.LoadAsync([], "https://example.com/a/b", Fetch, CancellationToken.None));
        Assert.Equal(["https://example.com/favicon.ico"], fetched);

        fetched.Clear();
        byte[]? icon = await FaviconLoader.LoadAsync(
            ["https://example.com/icon.svg", "https://example.com/icon.png"], "https://example.com/", Fetch, CancellationToken.None);
        Assert.Equal([1, 2, 3], icon);
        Assert.Equal(["https://example.com/icon.png"], fetched);
    }

    [Fact]
    public async Task DataUrlFaviconsDecodeWithoutFetching()
    {
        byte[]? icon = await FaviconLoader.LoadAsync(
            ["data:image/png;base64,AQID"],
            "https://example.com/",
            (_, _) => throw new InvalidOperationException("data URLs must not be fetched"),
            CancellationToken.None);

        Assert.Equal([1, 2, 3], icon);
    }
}
