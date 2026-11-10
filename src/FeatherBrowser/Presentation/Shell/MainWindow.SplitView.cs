using System.Windows;
using System.Windows.Controls;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private BrowserTab? _splitLeft;
    private BrowserTab? _splitRight;

    private BrowserTab? SplitPartner(BrowserTab tab)
    {
        if (_splitLeft is null || _splitRight is null || _splitLeft.IsClosed || _splitRight.IsClosed)
            return null;

        return tab == _splitLeft ? _splitRight : tab == _splitRight ? _splitLeft : null;
    }

    private bool IsShownInSplit(BrowserTab tab) =>
        _activeTab is { } active && SplitPartner(active) == tab;

    private async Task OpenSplitViewAsync(BrowserTab other)
    {
        if (_activeTab is not { } active || other == active || other.IsClosed)
            return;

        if (SplitPartner(active) is { } oldPartner && oldPartner != other)
        {
            _splitLeft = _splitRight = null;
            MoveTabToBackground(oldPartner);
        }

        _splitLeft = active;
        _splitRight = other;
        CancelSleepSchedule(other);

        if (!await TryEnsureTabLoadedAsync(other) || other.IsClosed || _activeTab != active)
        {
            _splitLeft = _splitRight = null;
            ApplySplitLayout();
            return;
        }

        SplitLeftColumn.Width = new GridLength(1, GridUnitType.Star);
        SplitRightColumn.Width = new GridLength(1, GridUnitType.Star);
        ApplySplitLayout();
        StatusText.Text = "Split view · drag the divider to resize, click a side to use it";
    }

    private void CloseSplitView()
    {
        BrowserTab? left = _splitLeft;
        BrowserTab? right = _splitRight;
        _splitLeft = _splitRight = null;
        ApplySplitLayout();

        foreach (BrowserTab? tab in new[] { left, right })
        {
            if (tab is { IsClosed: false, IsLoaded: true } && tab != _activeTab)
                MoveTabToBackground(tab);
        }
    }

    private void HideSplitPartner()
    {
        if (_activeTab is { } active && SplitPartner(active) is { IsLoaded: true } partner)
            partner.View.SetVisible(false, zIndex: 0);
    }

    private void ApplySplitLayout()
    {
        BrowserTab? partner = _activeTab is { } active ? SplitPartner(active) : null;
        bool split = partner is not null;

        SplitDividerColumn.Width = split ? GridLength.Auto : new GridLength(0);
        if (!split)
            SplitRightColumn.Width = new GridLength(0);
        else if (SplitRightColumn.Width.Value == 0)
            SplitRightColumn.Width = new GridLength(1, GridUnitType.Star);
        SplitDivider.Visibility = split ? Visibility.Visible : Visibility.Collapsed;

        foreach (BrowserTab tab in _tabs)
        {
            if (!tab.View.HasControl)
                continue;

            Grid.SetColumn(tab.View.Control, split && tab == _splitRight ? 2 : 0);
        }
        
        foreach (BrowserTab? member in new[] { _splitLeft, _splitRight })
        {
            if (member is { IsClosed: false, IsLoaded: true } && member != _activeTab && member != partner &&
                member.View.IsVisible)
            {
                member.HiddenSinceUtc = DateTime.UtcNow;
                member.View.SetVisible(false, zIndex: 0);
                ScheduleBackgroundLifecycle(member);
            }
        }

        if (partner is { IsLoaded: true } &&
            CommandPaletteOverlay.Visibility != Visibility.Visible &&
            WindowState != WindowState.Minimized)
        {
            CancelSleepSchedule(partner);
            partner.View.Resume();
            partner.View.SetVisible(true, zIndex: 10);
        }
    }
}
