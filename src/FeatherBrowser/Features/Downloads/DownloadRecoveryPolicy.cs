using FeatherBrowser.Domain.Models;

namespace FeatherBrowser.Features.Downloads;

internal static class DownloadRecoveryPolicy
{
    private const string InProgressState = "In progress";
    private const string InterruptedState = "Interrupted";

    public static int MarkOrphanedAsInterrupted(
        IList<DownloadEntry> downloads,
        DateTimeOffset? completedAt = null)
    {
        ArgumentNullException.ThrowIfNull(downloads);

        DateTimeOffset timestamp = completedAt ?? DateTimeOffset.Now;
        int changed = 0;

        foreach (DownloadEntry download in downloads)
        {
            if (!string.Equals(
                    download.State,
                    InProgressState,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            download.State = InterruptedState;
            download.CompletedAt ??= timestamp;
            changed++;
        }

        return changed;
    }
}
