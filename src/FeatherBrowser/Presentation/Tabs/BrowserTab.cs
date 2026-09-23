using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace FeatherBrowser.Presentation.Tabs;

internal sealed class BrowserTab
{
    public required Guid Id { get; init; }
    public required WebView2 View { get; set; }
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
    public bool IsClosed { get; set; }
    public bool IsLoaded { get; set; }
    public bool IsPinned { get; set; }
    public bool IsPlayingAudio { get; set; }
    public bool HasActiveDownload { get; set; }
    public bool NeedsContentRestore { get; set; } = true;
    public int BlockedRequests { get; set; }
    public DateTime HiddenSinceUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActivatedUtc { get; set; } = DateTime.UtcNow;
    public string Workspace { get; set; } = "Main";

    public bool IsInternalPage => IsStartPage || IsSettingsPage || IsLibraryPage || IsWelcomePage;
    public bool IsCold => !IsLoaded;

    public void SetSelected(bool selected, Brush active, Brush inactive, Brush? accent = null)
    {
        Header.Background = selected ? active : inactive;
        Header.BorderBrush = selected && accent is not null ? accent : new SolidColorBrush(Color.FromRgb(31, 42, 54));
        Header.BorderThickness = selected ? new System.Windows.Thickness(1, 1, 1, 2) : new System.Windows.Thickness(1);
        UpdateStateIndicator();
    }

    public void UpdateStateIndicator()
    {
        if (IsPinned)
        {
            StateIndicator.Text = "◆";
            StateIndicator.ToolTip = "Pinned tab";
            return;
        }

        if (IsCold)
        {
            StateIndicator.Text = "○";
            StateIndicator.ToolTip = "Unloaded to save memory";
            return;
        }

        if (IsPlayingAudio)
        {
            StateIndicator.Text = "♪";
            StateIndicator.ToolTip = "Playing audio";
            return;
        }

        StateIndicator.Text = string.Empty;
        StateIndicator.ToolTip = null;
    }
}
