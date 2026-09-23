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
    private void ApplyPerformanceToTabs()
    {
        bool shouldManage = _ecoMode || _store.Settings.LowMemoryMode || _store.Settings.GameMode;
        foreach (BrowserTab tab in _tabs.Where(t => !t.IsClosed))
        {
            if (!tab.IsLoaded)
            {
                tab.UpdateStateIndicator();
                continue;
            }

            if (tab == _activeTab && WindowState != WindowState.Minimized)
            {
                tab.SleepCancellation?.Cancel();
                tab.SleepCancellation?.Dispose();
                tab.SleepCancellation = null;
                if (tab.View.CoreWebView2.IsSuspended)
                    tab.View.CoreWebView2.Resume();
                if (_store.Settings.MuteBackgroundTabs || _store.Settings.GameMode)
                    tab.View.CoreWebView2.IsMuted = false;
                continue;
            }

            tab.View.CoreWebView2.IsMuted = _store.Settings.MuteBackgroundTabs || _store.Settings.GameMode;

            if (shouldManage && !IsProtectedTab(tab))
                ScheduleBackgroundLifecycle(tab);
            else
            {
                tab.SleepCancellation?.Cancel();
                tab.SleepCancellation?.Dispose();
                tab.SleepCancellation = null;
                if (tab.View.CoreWebView2.IsSuspended)
                    tab.View.CoreWebView2.Resume();
            }
        }

        EnforceLoadedTabBudget();
    }

    private void ToggleGameMode()
    {
        _store.Settings.GameMode = !_store.Settings.GameMode;
        if (_store.Settings.GameMode)
        {
            _store.Settings.EcoMode = true;
            _store.Settings.LowMemoryMode = true;
            _ecoMode = true;
        }
        _store.SaveSettings();
        ApplyRuntimeSettings();
        StatusText.Text = _store.Settings.GameMode
            ? "Gaming Mode enabled · only the active tab stays hot when possible"
            : "Gaming Mode disabled";
    }

    private readonly FeatherBrowser.Features.WebView3.ResourceSampler _resourceSampler = new();
    private FeatherBrowser.Features.WebView3.ResourceSnapshot _resourceSnapshot = FeatherBrowser.Features.WebView3.ResourceSnapshot.Empty;
    private bool _samplingResources;
    private DateTime _nextResourceSampleUtc = DateTime.MinValue;

    private async void UpdateResourceText()
    {
        if (_environment is null ||
            System.Threading.Volatile.Read(ref _isClosing) ||
            _samplingResources)
        {
            return;
        }

        PublishResourceSnapshot();

        if (DateTime.UtcNow < _nextResourceSampleUtc)
            return;

        _samplingResources = true;

        try
        {
            var ids = new Dictionary<int, string>
            {
                [Environment.ProcessId] = "Feather UI"
            };

            foreach (CoreWebView2ProcessInfo info in _environment.GetProcessInfos())
                ids.TryAdd(info.ProcessId, info.Kind.ToString());

            _resourceSnapshot = await Task.Run(
                () => _resourceSampler.Sample(ids));

            if (System.Threading.Volatile.Read(ref _isClosing))
                return;

            _lastObservedMemoryBytes = _resourceSnapshot.MemoryBytes;

            PublishResourceSnapshot();

            if (!_resourceSnapshot.IsPartial &&
                _resourceSnapshot.Processes.Count > 0)
            {
                ApplyMemoryGuard(_lastObservedMemoryBytes);
            }

            EnforceLoadedTabBudget();
        }
        catch (COMException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Resource sampling failed due to a WebView2 COM error: {ex.Message}");

            StatusText.Text = "Resource sample unavailable; browser remains active";
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Resource sampling failed due to an invalid state: {ex.Message}");

            StatusText.Text = "Resource sample unavailable; browser remains active";
        }
        catch (Win32Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Resource sampling failed due to a Windows process error: {ex.Message}");

            StatusText.Text = "Resource sample unavailable; browser remains active";
        }
        finally
        {
            _samplingResources = false;

            if (!System.Threading.Volatile.Read(ref _isClosing))
            {
                int interval =
                    FeatherBrowser.Features.WebView3.PerformancePolicy
                        .SampleIntervalSeconds(
                            WindowState == WindowState.Minimized);

                _nextResourceSampleUtc =
                    DateTime.UtcNow.AddSeconds(interval);

                _resourceTimer.Interval =
                    TimeSpan.FromSeconds(interval);
            }
        }
    }
    private void PublishResourceSnapshot()
    {
        int loaded = _tabs.Count(t => !t.IsClosed && t.IsLoaded);
        int cold = _tabs.Count(t => !t.IsClosed && !t.IsLoaded);
        string cpu = _resourceSnapshot.CpuPercent is double value ? $"{value:0.0}% CPU" : "CPU warming up";
        if (StatusBar.Visibility == Visibility.Visible)
            ResourceText.Text = $"{FormatBytes(_resourceSnapshot.MemoryBytes)} {_resourceSnapshot.MemoryLabel.ToLowerInvariant()}{(_resourceSnapshot.IsPartial ? " (partial)" : "")} · {cpu} · {loaded} loaded · {cold} cold";
        if (WindowState != WindowState.Minimized && _activeTab is { IsLoaded: true, IsStartPage: true } home)
            SendHomeStats(home);
    }

    private static string FormatBytes(long bytes)
    {
        double mb = bytes / 1024d / 1024d;
        return mb >= 1024 ? $"{mb / 1024d:0.0} GB" : $"{mb:0} MB";
    }

    private void ApplyMemoryGuard(long totalBytes)
    {
        if (!_store.Settings.AutoMemoryGuard || (!_store.Settings.LowMemoryMode && !_store.Settings.GameMode))
            return;

        int configuredLimitMb = Math.Clamp(_store.Settings.MemoryGuardMb, 350, 8192);
        int effectiveLimitMb = _store.Settings.GameMode ? Math.Min(configuredLimitMb, 600) : configuredLimitMb;
        long limitBytes = effectiveLimitMb * 1024L * 1024L;
        if (totalBytes <= limitBytes || DateTime.UtcNow - _lastMemoryGuardUtc < TimeSpan.FromSeconds(12))
            return;

        _lastMemoryGuardUtc = DateTime.UtcNow;
        int unloaded = 0;
        foreach (BrowserTab tab in _tabs
                     .Where(t => t != _activeTab && t.IsLoaded && !t.IsClosed)
                     .OrderBy(t => t.LastActivatedUtc)
                     .ToList())
        {
            if (ColdUnloadTab(tab))
                unloaded++;
            if (_tabs.Count(t => !t.IsClosed && t.IsLoaded) <= 1)
                break;
        }

        if (unloaded > 0)
            StatusText.Text = $"Memory Guard unloaded {unloaded} tab{(unloaded == 1 ? "" : "s")} at {FormatBytes(totalBytes)}";
    }

    private void ResourceText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ShowPerformanceCenter();

    private void ShowPerformanceCenter()
    {
        var window = new Window
        {
            Title = "Feather Performance Center",
            Width = 860,
            Height = 620,
            MinWidth = 700,
            MinHeight = 480,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResize,
            Background = new SolidColorBrush(Color.FromRgb(10, 20, 34)),
            Foreground = (Brush)FindResource("TextPrimary")
        };
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        var frame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(10, 20, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(62, 87, 112)),
            BorderThickness = new Thickness(1)
        };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.Child = root;

        var titleBar = new Border { Background = new SolidColorBrush(Color.FromRgb(16, 32, 51)) };
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                window.DragMove();
        };
        var titleGrid = new Grid { Margin = new Thickness(12, 0, 4, 0) };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromRgb(23, 59, 92)),
            VerticalAlignment = VerticalAlignment.Center
        };
        mark.Child = new TextBlock { Text = "F", Foreground = new SolidColorBrush(Color.FromRgb(168, 212, 255)), FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        titleGrid.Children.Add(mark);
        var title = new TextBlock { Text = "Performance Center", Foreground = (Brush)FindResource("TextPrimary"), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
        Grid.SetColumn(title, 1);
        titleGrid.Children.Add(title);
        var titleClose = new Button { Content = "×", Style = (Style)FindResource("IconButton"), Width = 34, Height = 30, FontSize = 15 };
        titleClose.Click += (_, _) => window.Close();
        Grid.SetColumn(titleClose, 2);
        titleGrid.Children.Add(titleClose);
        titleBar.Child = titleGrid;
        root.Children.Add(titleBar);

        var header = new StackPanel { Margin = new Thickness(22, 20, 22, 16) };
        Grid.SetRow(header, 1);
        var heading = new TextBlock { Text = "Memory & processes", FontSize = 25, FontWeight = FontWeights.SemiBold };
        var summary = new TextBlock { Foreground = _secondaryBrush, Margin = new Thickness(0, 6, 0, 0), FontSize = 11.5 };
        header.Children.Add(heading);
        header.Children.Add(summary);
        root.Children.Add(header);

        var columns = new Grid { Margin = new Thickness(22, 0, 22, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });
        Grid.SetRow(columns, 2);
        root.Children.Add(columns);

        ListBox MakeList()
        {
            return new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(23, 44, 66)),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 87, 112)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4)
            };
        }

        var tabsPanel = new DockPanel { Margin = new Thickness(0, 0, 7, 0) };
        var tabsTitle = new TextBlock { Text = "TABS", Foreground = new SolidColorBrush(Color.FromRgb(111, 128, 149)), FontWeight = FontWeights.Bold, FontSize = 9.5, Margin = new Thickness(5, 0, 0, 8) };
        DockPanel.SetDock(tabsTitle, Dock.Top);
        tabsPanel.Children.Add(tabsTitle);
        var tabList = MakeList();
        tabsPanel.Children.Add(tabList);
        columns.Children.Add(tabsPanel);

        var processPanel = new DockPanel { Margin = new Thickness(7, 0, 0, 0) };
        Grid.SetColumn(processPanel, 1);
        var processTitle = new TextBlock { Text = "WEBVIEW2 PROCESSES", Foreground = new SolidColorBrush(Color.FromRgb(111, 128, 149)), FontWeight = FontWeights.Bold, FontSize = 9.5, Margin = new Thickness(5, 0, 0, 8) };
        DockPanel.SetDock(processTitle, Dock.Top);
        processPanel.Children.Add(processTitle);
        var processList = MakeList();
        processPanel.Children.Add(processList);
        columns.Children.Add(processPanel);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(22, 16, 22, 20) };
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Button MakeButton(string text, bool accent = false)
        {
            return new Button
            {
                Content = text,
                Style = (Style)FindResource("ChromeButton"),
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(accent ? Color.FromRgb(20, 52, 81) : Color.FromRgb(16, 23, 32)),
                Foreground = accent ? new SolidColorBrush(Color.FromRgb(159, 208, 255)) : (Brush)FindResource("TextPrimary"),
                Cursor = Cursors.Hand
            };
        }

        void Refresh()
        {
            tabList.Items.Clear();
            foreach (BrowserTab tab in _tabs.Where(t => !t.IsClosed))
            {
                string state;
                if (tab == _activeTab)
                    state = "ACTIVE";
                else if (!tab.IsLoaded)
                    state = "COLD";
                else
                {
                    try { state = tab.View.CoreWebView2.IsSuspended ? "SLEEP" : "HOT"; }
                    catch { state = "HOT"; }
                }
                string markers = (tab.IsPinned ? " · pinned" : "") + (tab.IsPlayingAudio ? " · audio" : "");
                tabList.Items.Add(new ListBoxItem
                {
                    Content = $"{state,-6}  {TrimLabel(tab.Title.Text, 42)}\n          {tab.Workspace}{markers}",
                    Tag = tab,
                    Padding = new Thickness(8, 7, 8, 7),
                    Background = Brushes.Transparent,
                    Foreground = (Brush)FindResource("TextPrimary")
                });
            }

            processList.Items.Clear();
            UpdateResourceText();
            foreach (var sample in _resourceSnapshot.Processes)
            {
                string privateRam = sample.PrivateWorkingSet is long ram ? FormatBytes(ram) : "unavailable";
                string cpu = sample.CpuPercent is double usage ? $"{usage:0.0}%" : "warming up";
                processList.Items.Add($"{sample.Kind} · PID {sample.Id}\nPrivate RAM {privateRam} · CPU {cpu}\nPrivate commit {FormatBytes(sample.PrivateCommit)}");
            }
            int loaded = _tabs.Count(t => !t.IsClosed && t.IsLoaded);
            int cold = _tabs.Count(t => !t.IsClosed && !t.IsLoaded);
            summary.Text = $"{_resourceSnapshot.MemoryLabel}: {FormatBytes(_resourceSnapshot.MemoryBytes)} · {loaded} loaded · {cold} cold · {_resourceSnapshot.Processes.Count} processes\n" +
                $"Combined working sets: {FormatBytes(_resourceSnapshot.WorkingSetBytes)} (shared pages may be counted repeatedly)" +
                (_resourceSnapshot.IsPartial ? " · partial sample" : "");
        }

        var unload = MakeButton("Unload selected");
        unload.Click += (_, _) =>
        {
            if (tabList.SelectedItem is not ListBoxItem item || item.Tag is not BrowserTab tab)
                return;
            if (tab == _activeTab)
            {
                StatusText.Text = "The active tab cannot be cold-unloaded while you are using it";
                return;
            }
            if (!ColdUnloadTab(tab) && tab.IsLoaded)
                HibernateTab(tab);
            Refresh();
        };
        buttons.Children.Add(unload);

        var trim = MakeButton("Trim background memory", true);
        trim.Click += (_, _) => { TrimMemoryNow(); Refresh(); };
        buttons.Children.Add(trim);

        var refresh = MakeButton("Refresh");
        refresh.Click += (_, _) => Refresh();
        buttons.Children.Add(refresh);

        var close = MakeButton("Close");
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(close);

        window.Content = frame;
        var refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        refreshTimer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => refreshTimer.Stop();
        Refresh();
        refreshTimer.Start();
        window.ShowDialog();
    }
}
