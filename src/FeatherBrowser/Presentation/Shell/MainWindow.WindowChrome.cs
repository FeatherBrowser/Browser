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
    private bool _manualFullscreen;
    private bool _changingFullscreen;
    private BrowserTab? _videoFullscreenTab;
    private WindowChrome? _chromeBeforeFullscreen;
    private WindowStyle _styleBeforeFullscreen;
    private ResizeMode _resizeBeforeFullscreen;
    private Rect _boundsBeforeFullscreen;

    private void ToggleFullscreen()
    {
        if (_videoFullscreenTab is { } videoTab)
        {
            _manualFullscreen = false;
            LeaveVideoFullscreen(videoTab);
            return;
        }

        _manualFullscreen = !_manualFullscreen;
        ApplyFullscreen();
    }

    private void LeaveVideoFullscreen(BrowserTab tab)
    {
        if (!ReferenceEquals(_videoFullscreenTab, tab))
            return;

        _videoFullscreenTab = null;
        ApplyFullscreen();
        _ = ExitDocumentFullscreenAsync(tab);
    }

    private static Task ExitDocumentFullscreenAsync(BrowserTab tab)
    {
        if (!tab.IsClosed && tab.IsLoaded)
            tab.View.ExecuteScript(PageScripts.ExitFullscreen);
        return Task.CompletedTask;
    }

    private void ApplyFullscreen()
    {
        if (_isClosing || _changingFullscreen)
            return;

        bool fullscreen = _manualFullscreen ||
            (_videoFullscreenTab is { IsClosed: false, IsLoaded: true } &&
             ReferenceEquals(_videoFullscreenTab, _activeTab));

        if (_isFullscreen == fullscreen)
            return;

        _changingFullscreen = true;

        try
        {
            if (fullscreen)
            {
                _stateBeforeFullscreen = WindowState;
                _boundsBeforeFullscreen = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, ActualWidth, ActualHeight)
                    : RestoreBounds;
                _styleBeforeFullscreen = WindowStyle;
                _resizeBeforeFullscreen = ResizeMode;
                _chromeBeforeFullscreen = WindowChrome.GetWindowChrome(this);
                _isFullscreen = true;

                CloseCommandPalette();
                TitleBar.Visibility = Visibility.Collapsed;
                TabBar.Visibility = Visibility.Collapsed;
                Toolbar.Visibility = Visibility.Collapsed;
                StatusBar.Visibility = Visibility.Collapsed;
                WorkspaceSidebar.Visibility = Visibility.Collapsed;
                NavigationProgress.Visibility = Visibility.Collapsed;
                PermissionBar.Visibility = Visibility.Collapsed;
                FindBar.Visibility = Visibility.Collapsed;

                WindowState = WindowState.Normal;
                WindowChrome.SetWindowChrome(this, null);
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                _isFullscreen = false;
                WindowState = WindowState.Normal;
                WindowStyle = _styleBeforeFullscreen;
                ResizeMode = _resizeBeforeFullscreen;
                WindowChrome.SetWindowChrome(this, _chromeBeforeFullscreen);

                if (!_boundsBeforeFullscreen.IsEmpty)
                {
                    Left = _boundsBeforeFullscreen.Left;
                    Top = _boundsBeforeFullscreen.Top;
                    Width = _boundsBeforeFullscreen.Width;
                    Height = _boundsBeforeFullscreen.Height;
                }

                TitleBar.Visibility = Visibility.Visible;
                TabBar.Visibility = Visibility.Visible;
                Toolbar.Visibility = Visibility.Visible;
                StatusBar.Visibility = _store.Settings.ShowStatusBar
                    ? Visibility.Visible : Visibility.Collapsed;
                WorkspaceSidebar.Visibility = _store.Settings.ShowWorkspaceSidebar
                    ? Visibility.Visible : Visibility.Collapsed;
                WindowState = _stateBeforeFullscreen;
                RefreshPermissionBar(_activeTab);
                if (_activeTab is not null)
                    RefreshFindBar(_activeTab);
            }
        }
        finally
        {
            _changingFullscreen = false;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        RootPanel.Margin = maximized && !_isFullscreen
            ? new Thickness(
                SystemParameters.WindowResizeBorderThickness.Left + 2,
                SystemParameters.WindowResizeBorderThickness.Top + 2,
                SystemParameters.WindowResizeBorderThickness.Right + 2,
                SystemParameters.WindowResizeBorderThickness.Bottom + 2)
            : new Thickness(0);

        if (_changingFullscreen)
            return;

        _nextResourceSampleUtc = DateTime.MinValue;
        if (WindowState == WindowState.Minimized)
        {
            SaveSessionSnapshot();
            if (_activeTab is { IsLoaded: true } active)
            {
                active.HiddenSinceUtc = DateTime.UtcNow;
                active.View.SetVisible(false, zIndex: 0);
            }
            foreach (BrowserTab tab in _tabs.Where(t => t.IsLoaded && !t.IsClosed).ToList())
            {
                if (!_store.Settings.HibernateWhenMinimized || !HibernateTab(tab))
                    ScheduleBackgroundLifecycle(tab);
            }
        }
        else if (_activeTab is { IsClosed: false } active)
        {
            if (active.IsLoaded)
            {
                active.SleepCancellation?.Cancel();
                active.View.Resume();
                active.View.SetVisible(true, zIndex: 10);
            }
            else _ = SelectTabAsync(active);
        }
        UpdateResourceText();
    }

    private void SelectAdjacentTab(int direction)
    {
        if (_activeTab is null)
            return;

        List<BrowserTab> visibleTabs = _tabs.Where(IsTabInActiveWorkspace).ToList();
        if (visibleTabs.Count < 2)
            return;

        int current = visibleTabs.IndexOf(_activeTab);
        int next = (current + direction + visibleTabs.Count) % visibleTabs.Count;
        _ = SelectTabAsync(visibleTabs[next]);
    }
}


public partial class MainWindow
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmCornerRound = 2;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplySystemFrame();
        FeatherBrowser.Presentation.Themes.ThemeManager.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplySystemFrame();

    private void ApplySystemFrame()
    {
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindows())
            return;

        int dark = FeatherBrowser.Presentation.Themes.ThemeManager.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeLegacy, ref dark, sizeof(int));

        int corners = DwmCornerRound;
        DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref corners, sizeof(int));

        Color edge = FeatherBrowser.Presentation.Themes.ThemeManager.ColorOf(_isPrivateMode ? "PrivateChromeBrush" : "BorderBrush");
        int border = edge.R | (edge.G << 8) | (edge.B << 16);
        DwmSetWindowAttribute(hwnd, DwmBorderColor, ref border, sizeof(int));
    }
}
