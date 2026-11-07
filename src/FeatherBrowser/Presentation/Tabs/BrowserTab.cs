using System.Windows.Controls;
using System.Windows.Media;
using FeatherBrowser.Features.Engine;

namespace FeatherBrowser.Presentation.Tabs;

internal sealed class BrowserTab
{
    public required Guid Id { get; init; }
    public required WebPage View { get; set; }
    public required Border Header { get; init; }
    public required TextBlock Title { get; init; }
    public required TextBlock StateIndicator { get; init; }
    public Task? LoadingTask { get; set; }
    public CancellationTokenSource? SleepCancellation { get; set; }
    public string LastAddress { get; set; } = string.Empty;
    public bool IsStartPage { get; set; }
    public bool IsSettingsPage { get; set; }
    public bool IsLibraryPage { get; set; }
    public bool IsWelcomePage { get; set; }
    public string LibrarySection { get; set; } = "history";
    public string SettingsSection { get; set; } = "performance";
    public bool IsClosed { get; set; }
    public bool IsLoaded { get; set; }
    public bool IsPinned { get; set; }
    public bool IsPlayingAudio { get; set; }
    public bool UserMuted { get; set; }
    public int ActiveDownloadCount { get; private set; }
    public bool HasActiveDownload => ActiveDownloadCount > 0;
    public bool NeedsContentRestore { get; set; } = true;
    public string InternalPageToken { get; set; } = string.Empty;
    public int BlockedRequests { get; set; }
    public DateTime HiddenSinceUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActivatedUtc { get; set; } = DateTime.UtcNow;
    public string Workspace { get; set; } = "Main";
    public bool IsInternalPage => IsStartPage || IsSettingsPage || IsLibraryPage || IsWelcomePage;
    public bool IsCold => !IsLoaded;
    public bool IsSleeping => IsLoaded && View.IsSuspended;
    public BrowserTab? Opener { get; set; }

    public void BeginDownload() => ActiveDownloadCount++;

    public void ResetDownloads() => ActiveDownloadCount = 0;

    public void CompleteDownload()
    {
        if (ActiveDownloadCount > 0)
            ActiveDownloadCount--;
    }

    public bool IsSelected { get; private set; }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        UpdateStateIndicator();
    }

    public void UpdateStateIndicator()
    {
        Header.Opacity = IsCold && !IsSelected ? 0.72 : 1;

        if (IsLoaded && (IsPlayingAudio || UserMuted))
        {
            bool muted = View.IsMuted;
            StateIndicator.Text = muted ? "" : "";
            StateIndicator.ToolTip = UserMuted
                ? "Muted · click to unmute"
                : muted ? "Playing audio (muted in background) · click to mute this tab" : "Playing audio · click to mute";
            return;
        }

        if (IsPinned)
        {
            StateIndicator.Text = "";
            StateIndicator.ToolTip = IsCold ? "Pinned · unloaded" : "Pinned tab";
            return;
        }

        if (IsCold)
        {
            StateIndicator.Text = string.Empty;
            StateIndicator.ToolTip = null;
            return;
        }

        if (IsSleeping)
        {
            StateIndicator.Text = "";
            StateIndicator.ToolTip = "Sleeping · frozen until you return";
            return;
        }

        StateIndicator.Text = string.Empty;
        StateIndicator.ToolTip = null;
    }
}
