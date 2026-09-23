using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Windows;
using FeatherBrowser.Presentation.Tabs;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private void ScheduleBackgroundLifecycle(BrowserTab tab)
    {
        tab.SleepCancellation?.Cancel();
        tab.SleepCancellation?.Dispose();
        tab.SleepCancellation = null;

        if (tab.IsClosed || !tab.IsLoaded || IsProtectedTab(tab) || (!_ecoMode && !_store.Settings.LowMemoryMode && !_store.Settings.GameMode))
            return;

        var cts = new CancellationTokenSource();
        tab.SleepCancellation = cts;
        _ = ManageBackgroundTabAsync(tab, cts.Token);
    }

    private async Task ManageBackgroundTabAsync(BrowserTab tab, CancellationToken token)
    {
        var view = tab.View;
        try
        {
            int sleepDelay = _store.Settings.GameMode ? 1 : Math.Clamp(_store.Settings.SleepAfterSeconds, 1, 60);
            await Task.Delay(TimeSpan.FromSeconds(sleepDelay), token);
            if (token.IsCancellationRequested || tab.View != view || !CanManageBackgroundTab(tab)) return;

            if (_ecoMode || _store.Settings.GameMode)
            {
                await view.CoreWebView2.TrySuspendAsync();
                if (tab.View != view || tab.IsClosed || !tab.IsLoaded) return;
                if (token.IsCancellationRequested || !CanManageBackgroundTab(tab))
                {
                    if (view.CoreWebView2.IsSuspended) view.CoreWebView2.Resume();
                    return;
                }
            }
            if (!_store.Settings.LowMemoryMode && !_store.Settings.GameMode) return;
            int unloadAfter = _store.Settings.GameMode ? 8 : Math.Clamp(_store.Settings.UnloadAfterSeconds, 2, 300);
            int grace = _store.Settings.GameMode ? 0 : Math.Clamp(_store.Settings.BackgroundGraceSeconds, 0, 60);
            int remaining = Math.Max(0, Math.Max(unloadAfter, grace) - sleepDelay);
            if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds(remaining), token);
            if (!token.IsCancellationRequested && tab.View == view && CanColdUnload(tab)) ColdUnloadTab(tab);
        }
        catch (Exception ex) when (ex is OperationCanceledException or COMException or InvalidOperationException)
        {
        }
        finally
        {
            if (!_isClosing) UpdateResourceText();
        }
    }

    private bool IsProtectedTab(BrowserTab tab) => tab.IsPinned || tab.HasActiveDownload ||
        (_store.Settings.KeepAudioTabsLoaded && tab.IsPlayingAudio) || IsKeepAliveTab(tab);

    private bool CanManageBackgroundTab(BrowserTab tab) =>
        !tab.IsClosed && tab.IsLoaded && !IsProtectedTab(tab) &&
        (_activeTab != tab || WindowState == WindowState.Minimized) && tab.View.Visibility != Visibility.Visible;

    private bool CanColdUnload(BrowserTab tab)
    {
        if (!CanManageBackgroundTab(tab) || tab.IsPinned || tab.HasActiveDownload)
            return false;
        if (_store.Settings.KeepAudioTabsLoaded && tab.IsPlayingAudio)
            return false;
        if (IsKeepAliveTab(tab))
            return false;
        return true;
    }

    private bool ColdUnloadTab(BrowserTab tab)
    {
        if (!CanColdUnload(tab))
            return false;

        try
        {
            if (!tab.IsInternalPage && tab.View.CoreWebView2 is not null)
            {
                string source = tab.View.CoreWebView2.Source ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(source) &&
                    !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(source, "about:blank", StringComparison.OrdinalIgnoreCase))
                {
                    tab.LastAddress = source;
                }
            }
        }
        catch (ObjectDisposedException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Could not preserve tab address because WebView2 was disposed: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Could not preserve tab address due to an invalid WebView2 state: {ex.Message}");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Could not preserve tab address due to a WebView2 COM error: {ex.Message}");
        }

        tab.SleepCancellation?.Cancel();
        tab.SleepCancellation?.Dispose();
        tab.SleepCancellation = null;
        BrowserHost.Children.Remove(tab.View);
        tab.View.Dispose();
        tab.View = CreateWebViewControl();
        tab.IsLoaded = false;
        tab.IsPlayingAudio = false;
        tab.NeedsContentRestore = true;
        tab.UpdateStateIndicator();
        UpdateResourceText();
        return true;
    }

    private bool IsKeepAliveTab(BrowserTab tab)
    {
        if (tab.IsInternalPage ||
            string.IsNullOrWhiteSpace(tab.LastAddress) ||
            _store.Settings.KeepAliveSites.Count == 0)
        {
            return false;
        }

        string host = SafeHost(tab.LastAddress);

        if (host == "page")
        {
            return false;
        }

        return _store.Settings.KeepAliveSites
            .Select(raw => raw.Trim().TrimStart('.'))
            .Where(allowed => allowed.Length > 0)
            .Any(allowed =>
                host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith('.' + allowed, StringComparison.OrdinalIgnoreCase));
    }
    private bool HibernateTab(BrowserTab tab)
    {
        if (tab.IsClosed || !tab.IsLoaded || IsProtectedTab(tab))
            return false;
        if (_store.Settings.KeepAudioTabsLoaded && tab.IsPlayingAudio)
            return false;

        try
        {
            if (!tab.IsInternalPage && tab.View.CoreWebView2 is not null)
            {
                string source = tab.View.CoreWebView2.Source ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(source) &&
                    !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(source, "about:blank", StringComparison.OrdinalIgnoreCase))
                {
                    tab.LastAddress = source;
                }
            }
        }
        catch (ObjectDisposedException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Could not preserve tab address because WebView2 was disposed: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Could not preserve tab address due to an invalid WebView2 state: {ex.Message}");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Could not preserve tab address due to a WebView2 COM error: {ex.Message}");
        }

        tab.SleepCancellation?.Cancel();
        tab.SleepCancellation?.Dispose();
        tab.SleepCancellation = null;
        BrowserHost.Children.Remove(tab.View);
        tab.View.Dispose();
        tab.View = CreateWebViewControl();
        tab.IsLoaded = false;
        tab.IsPlayingAudio = false;
        tab.NeedsContentRestore = true;
        tab.UpdateStateIndicator();
        return true;
    }

    private int LoadedTabBudget
    {
        get
        {
            if (_store.Settings.GameMode)
                return 1;
            if (!_store.Settings.LowMemoryMode)
                return int.MaxValue;

            int budget = Math.Clamp(_store.Settings.MaxLoadedTabs, 1, 8);
            if (!_store.Settings.AdaptiveMemoryMode || _lastObservedMemoryBytes <= 0)
                return budget;

            long guard = Math.Max(350, _store.Settings.MemoryGuardMb) * 1024L * 1024L;
            double pressure = guard <= 0 ? 0 : (double)_lastObservedMemoryBytes / guard;
            if (pressure >= 0.90) return 1;
            if (pressure >= 0.65) return Math.Max(1, budget / 2);
            return budget;
        }
    }

    private void EnforceLoadedTabBudget()
    {
        int budget = LoadedTabBudget;
        if (budget == int.MaxValue)
            return;

        while (_tabs.Count(t => !t.IsClosed && t.IsLoaded) > budget)
        {
            BrowserTab? candidate = _tabs
                .Where(t => CanColdUnload(t) && FeatherBrowser.Features.WebView3.PerformancePolicy.GraceElapsed(
                    t.HiddenSinceUtc, DateTime.UtcNow, _store.Settings.BackgroundGraceSeconds, _store.Settings.GameMode))
                .OrderBy(t => t.LastActivatedUtc)
                .FirstOrDefault();
            if (candidate is null || !ColdUnloadTab(candidate))
                break;
        }
    }

    private void TrimMemoryNow()
    {
        int unloaded = 0;
        foreach (BrowserTab tab in _tabs.Where(t => !t.IsClosed && t != _activeTab && t.IsLoaded).OrderBy(t => t.LastActivatedUtc).ToList())
        {
            if (ColdUnloadTab(tab))
                unloaded++;
        }

        StatusText.Text = unloaded == 0 ? "Memory already trimmed" : $"Unloaded {unloaded} background tab{(unloaded == 1 ? "" : "s")}";
        UpdateResourceText();
    }
}
