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
    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        WindowChrome? chrome = WindowChrome.GetWindowChrome(this);

        if (_isFullscreen)
        {
            _stateBeforeFullscreen = WindowState;
            TitleBar.Visibility = Visibility.Collapsed;
            TabBar.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            WorkspaceSidebar.Visibility = Visibility.Collapsed;
            if (chrome is not null)
            {
                chrome.CaptionHeight = 0;
                chrome.ResizeBorderThickness = new Thickness(0);
            }
            WindowState = WindowState.Maximized;
        }
        else
        {
            TitleBar.Visibility = Visibility.Visible;
            TabBar.Visibility = Visibility.Visible;
            Toolbar.Visibility = Visibility.Visible;
            StatusBar.Visibility = _store.Settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
            WorkspaceSidebar.Visibility = _store.Settings.ShowWorkspaceSidebar ? Visibility.Visible : Visibility.Collapsed;
            if (chrome is not null)
            {
                chrome.CaptionHeight = 52;
                chrome.ResizeBorderThickness = new Thickness(6);
            }
            WindowState = _stateBeforeFullscreen;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && TabBar.IsAncestorOf(source))
            return;
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Normal)
            DragMove();
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
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";

        _nextResourceSampleUtc = DateTime.MinValue;
        if (WindowState == WindowState.Minimized)
        {
            SaveSessionSnapshot();
            if (_activeTab is { IsLoaded: true } active)
            {
                active.HiddenSinceUtc = DateTime.UtcNow;
                active.View.Visibility = Visibility.Collapsed;
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
                if (active.View.CoreWebView2.IsSuspended) active.View.CoreWebView2.Resume();
                active.View.Visibility = Visibility.Visible;
            }
            else _ = SelectTabAsync(active);
        }
        UpdateResourceText();
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (CommandPaletteOverlay.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            CloseCommandPalette();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.K)
        {
            OpenCommandPalette();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.B)
        {
            _store.Settings.ShowWorkspaceSidebar = !_store.Settings.ShowWorkspaceSidebar;
            _store.SaveSettings();
            ApplyUiPreferences();
            UpdateWorkspaceSidebar();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.L)
        {
            AddressBox.Focus();
            AddressBox.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.N)
        {
            OpenPrivateWindow();
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.T)
        {
            if (_closedTabs.Count > 0)
                await AddTabAsync(_closedTabs.Pop());
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.T)
        {
            await AddTabAsync();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.W && _activeTab is not null)
        {
            await CloseTabAsync(_activeTab);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Tab)
        {
            SelectAdjacentTab(shift ? -1 : 1);
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.M)
        {
            TrimMemoryNow();
            e.Handled = true;
        }
        else if (shift && e.Key == Key.Escape)
        {
            ShowPerformanceCenter();
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.G)
        {
            ToggleGameMode();
            if (_activeTab?.IsStartPage == true)
                ShowStartPage(_activeTab);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemComma)
        {
            await OpenSettingsAsync();
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.P)
        {
            await AutofillSavedPasswordAsync();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.D)
        {
            ToggleCurrentBookmark();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.H)
        {
            await OpenLibraryAsync("history");
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.J)
        {
            await OpenLibraryAsync("downloads");
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.O)
        {
            await OpenLibraryAsync("favorites");
            e.Handled = true;
        }
        else if ((ctrl && e.Key == Key.R) || e.Key == Key.F5)
        {
            ReloadButton_Click(sender, e);
            e.Handled = true;
        }
        else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            ChangeZoom(0.1);
            e.Handled = true;
        }
        else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            ChangeZoom(-0.1);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.D0)
        {
            if (_activeTab is { IsLoaded: true })
                _activeTab.View.ZoomFactor = 1.0;
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Left)
        {
            BackButton_Click(sender, e);
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Right)
        {
            ForwardButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void ChangeZoom(double delta)
    {
        if (_activeTab is not { IsLoaded: true })
            return;
        _activeTab.View.ZoomFactor = Math.Clamp(_activeTab.View.ZoomFactor + delta, 0.5, 2.5);
        StatusText.Text = $"Zoom {Math.Round(_activeTab.View.ZoomFactor * 100)}%";
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
