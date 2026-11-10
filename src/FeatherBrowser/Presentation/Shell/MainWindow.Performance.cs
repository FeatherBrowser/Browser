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
using FeatherBrowser.Features.Engine;
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
                CancelSleepSchedule(tab);
                tab.View.Resume();
                ApplyTabMute(tab);
                continue;
            }

            ApplyTabMute(tab);

            if (shouldManage && !IsProtectedTab(tab))
                ScheduleBackgroundLifecycle(tab);
            else
            {
                CancelSleepSchedule(tab);
                tab.View.Resume();
            }

            tab.UpdateStateIndicator();
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
        if (!_engineReady ||
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
            _resourceSnapshot = await Task.Run(
                () => _resourceSampler.Sample(EngineProcesses.Snapshot()));

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
            await SampleTabMemoryAsync();
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
            {
                unloaded++;
                break;
            }
        }

        if (unloaded > 0)
            StatusText.Text = $"Memory Guard unloaded {unloaded} tab{(unloaded == 1 ? "" : "s")} at {FormatBytes(totalBytes)}";
    }

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
            Background = (Brush)FindResource("ContentBrush"),
            Foreground = (Brush)FindResource("TextPrimary"),
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 12
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
            Background = (Brush)FindResource("ContentBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1)
        };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.Child = root;

        var titleBar = new Border
        {
            Background = (Brush)FindResource("ChromeBrush"),
            BorderBrush = (Brush)FindResource("DividerBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                window.DragMove();
        };
        var titleGrid = new Grid { Margin = new Thickness(12, 0, 0, 0) };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = "Performance", Foreground = (Brush)FindResource("TextSecondary"), VerticalAlignment = VerticalAlignment.Center };
        titleGrid.Children.Add(title);
        var titleClose = new Button { Content = "", Style = (Style)FindResource("CaptionCloseButton"), Height = 31 };
        titleClose.Click += (_, _) => window.Close();
        Grid.SetColumn(titleClose, 1);
        titleGrid.Children.Add(titleClose);
        titleBar.Child = titleGrid;
        root.Children.Add(titleBar);

        var header = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };
        Grid.SetRow(header, 1);
        var heading = new TextBlock { Text = "Memory and processes", FontSize = 15, FontWeight = FontWeights.SemiBold };
        var summary = new TextBlock { Foreground = _secondaryBrush, Margin = new Thickness(0, 4, 0, 0), FontSize = 11.5 };
        header.Children.Add(heading);
        header.Children.Add(summary);
        root.Children.Add(header);

        var columns = new Grid { Margin = new Thickness(16, 0, 16, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });
        Grid.SetRow(columns, 2);
        root.Children.Add(columns);

        ListBox MakeList()
        {
            return new ListBox
            {
                Background = (Brush)FindResource("ChromeBrush"),
                Foreground = (Brush)FindResource("TextPrimary"),
                BorderBrush = (Brush)FindResource("DividerBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3),
                ItemContainerStyle = (Style)FindResource("PlainListItemStyle")
            };
        }

        var tabsPanel = new DockPanel { Margin = new Thickness(0, 0, 7, 0) };
        var tabsTitle = new TextBlock { Text = "Tabs", Foreground = (Brush)FindResource("TextMuted"), FontWeight = FontWeights.SemiBold, FontSize = 11, Margin = new Thickness(2, 0, 0, 6) };
        DockPanel.SetDock(tabsTitle, Dock.Top);
        tabsPanel.Children.Add(tabsTitle);
        var tabList = MakeList();
        tabsPanel.Children.Add(tabList);
        columns.Children.Add(tabsPanel);

        var processPanel = new DockPanel { Margin = new Thickness(7, 0, 0, 0) };
        Grid.SetColumn(processPanel, 1);
        var processTitle = new TextBlock { Text = "Chromium processes", Foreground = (Brush)FindResource("TextMuted"), FontWeight = FontWeights.SemiBold, FontSize = 11, Margin = new Thickness(2, 0, 0, 6) };
        DockPanel.SetDock(processTitle, Dock.Top);
        processPanel.Children.Add(processTitle);
        var processList = MakeList();
        processPanel.Children.Add(processList);
        columns.Children.Add(processPanel);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 12, 16, 14) };
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Button MakeButton(string text, bool accent = false)
        {
            return new Button
            {
                Content = text,
                Style = (Style)FindResource(accent ? "PrimaryButton" : "SecondaryButton"),
                Margin = new Thickness(6, 0, 0, 0)
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
                    state = tab.View.IsSuspended ? "SLEEP" : "HOT";
                }
                string markers = (tab.IsPinned ? " · pinned" : "") + (tab.IsPlayingAudio ? " · audio" : "") +
                    (tab.IsLoaded && tab.PageMemoryBytes is long pageBytes ? $" · {FormatBytes(pageBytes)} page memory" : "");
                tabList.Items.Add(new ListBoxItem
                {
                    Content = $"{state,-6}  {TrimLabel(tab.Title.Text, 42)}\n          {tab.Workspace}{markers}",
                    Tag = tab,
                    Padding = new Thickness(8, 5, 8, 5),
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

