using FeatherBrowser.Domain.Models;
using FeatherBrowser.Features.Downloads;

namespace FeatherBrowser.Tests;

public sealed class DownloadRecoveryPolicyTests
{
    [Fact]
    public void StartupRecoveryInterruptsOnlyOrphanedDownloads()
    {
        DateTimeOffset timestamp = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        var downloads = new List<DownloadEntry>
        {
            new() { State = "In progress" },
            new() { State = "Completed" },
            new() { State = "Interrupted" }
        };

        int changed = DownloadRecoveryPolicy.MarkOrphanedAsInterrupted(
            downloads,
            timestamp);

        Assert.Equal(1, changed);
        Assert.Equal("Interrupted", downloads[0].State);
        Assert.Equal(timestamp, downloads[0].CompletedAt);
        Assert.Equal("Completed", downloads[1].State);
        Assert.Equal("Interrupted", downloads[2].State);
    }
}
