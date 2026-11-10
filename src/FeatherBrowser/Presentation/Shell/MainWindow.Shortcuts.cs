using System.Windows;
using System.Windows.Input;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private enum ShellCommand
    {
        CommandPalette,
        ToggleSidebar,
        FocusAddress,
        PrivateWindow,
        ReopenClosedTab,
        NewTab,
        CloseTab,
        NextTab,
        PreviousTab,
        SelectTab,
        TrimMemory,
        PerformanceCenter,
        ToggleGameMode,
        Settings,
        Autofill,
        Bookmark,
        History,
        Downloads,
        Favorites,
        Reload,
        HardReload,
        ZoomIn,
        ZoomOut,
        ZoomReset,
        Back,
        Forward,
        Home,
        Fullscreen,
        DevTools,
        Print,
        Find,
        FindNext,
        FindPrevious,
        SearchTabs,
    }

    private static (ShellCommand Command, int Argument)? MapShortcut(Key key, ModifierKeys modifiers)
    {
        bool ctrl = modifiers.HasFlag(ModifierKeys.Control);
        bool shift = modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = modifiers.HasFlag(ModifierKeys.Alt);
        bool none = modifiers == ModifierKeys.None;
        bool ctrlOnly = modifiers == ModifierKeys.Control;
        bool ctrlShift = modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
        bool altOnly = modifiers == ModifierKeys.Alt;

        if (ctrlOnly && key is >= Key.D1 and <= Key.D9)
            return (ShellCommand.SelectTab, key - Key.D1 + 1);

        return key switch
        {
            Key.K when ctrlOnly => (ShellCommand.CommandPalette, 0),
            Key.A when ctrlShift => (ShellCommand.SearchTabs, 0),
            Key.B when ctrlOnly => (ShellCommand.ToggleSidebar, 0),
            Key.L when ctrlOnly => (ShellCommand.FocusAddress, 0),
            Key.D when altOnly => (ShellCommand.FocusAddress, 0),
            Key.F6 when none => (ShellCommand.FocusAddress, 0),
            Key.N when ctrlShift => (ShellCommand.PrivateWindow, 0),
            Key.T when ctrlShift => (ShellCommand.ReopenClosedTab, 0),
            Key.T when ctrlOnly => (ShellCommand.NewTab, 0),
            Key.W when ctrlOnly => (ShellCommand.CloseTab, 0),
            Key.F4 when ctrlOnly => (ShellCommand.CloseTab, 0),
            Key.Tab when ctrlOnly => (ShellCommand.NextTab, 0),
            Key.Tab when ctrlShift => (ShellCommand.PreviousTab, 0),
            Key.PageDown when ctrlOnly => (ShellCommand.NextTab, 0),
            Key.PageUp when ctrlOnly => (ShellCommand.PreviousTab, 0),
            Key.M when ctrlShift => (ShellCommand.TrimMemory, 0),
            Key.Escape when modifiers == ModifierKeys.Shift => (ShellCommand.PerformanceCenter, 0),
            Key.G when ctrlShift => (ShellCommand.ToggleGameMode, 0),
            Key.OemComma when ctrlOnly => (ShellCommand.Settings, 0),
            Key.P when ctrlShift => (ShellCommand.Autofill, 0),
            Key.P when ctrlOnly => (ShellCommand.Print, 0),
            Key.D when ctrlOnly => (ShellCommand.Bookmark, 0),
            Key.H when ctrlOnly => (ShellCommand.History, 0),
            Key.J when ctrlOnly => (ShellCommand.Downloads, 0),
            Key.O when ctrlShift => (ShellCommand.Favorites, 0),
            Key.R when ctrlOnly => (ShellCommand.Reload, 0),
            Key.R when ctrlShift => (ShellCommand.HardReload, 0),
            Key.F5 when none => (ShellCommand.Reload, 0),
            Key.F5 when ctrlOnly || modifiers == ModifierKeys.Shift => (ShellCommand.HardReload, 0),
            Key.OemPlus or Key.Add when ctrl && !alt => (ShellCommand.ZoomIn, 0),
            Key.OemMinus or Key.Subtract when ctrl && !alt => (ShellCommand.ZoomOut, 0),
            Key.D0 or Key.NumPad0 when ctrlOnly => (ShellCommand.ZoomReset, 0),
            Key.Left when altOnly => (ShellCommand.Back, 0),
            Key.Right when altOnly => (ShellCommand.Forward, 0),
            Key.BrowserBack when none => (ShellCommand.Back, 0),
            Key.BrowserForward when none => (ShellCommand.Forward, 0),
            Key.Home when altOnly => (ShellCommand.Home, 0),
            Key.F11 when none => (ShellCommand.Fullscreen, 0),
            Key.F12 when none => (ShellCommand.DevTools, 0),
            Key.I when ctrlShift => (ShellCommand.DevTools, 0),
            Key.F when ctrlOnly => (ShellCommand.Find, 0),
            Key.F3 when none => (ShellCommand.FindNext, 0),
            Key.F3 when modifiers == ModifierKeys.Shift => (ShellCommand.FindPrevious, 0),
            Key.G when ctrlOnly => (ShellCommand.FindNext, 0),
            _ => null
        };
    }

    private static bool IsBrowserShortcut(Key key, ModifierKeys modifiers) =>
        MapShortcut(key, modifiers) is not null;

    private static bool IsReservedBrowserShortcut(Key key, ModifierKeys modifiers) =>
        MapShortcut(key, modifiers) is
        {
            Command: ShellCommand.NewTab or ShellCommand.CloseTab or ShellCommand.ReopenClosedTab or
                     ShellCommand.PrivateWindow or ShellCommand.NextTab or ShellCommand.PreviousTab or
                     ShellCommand.SelectTab or ShellCommand.FocusAddress
        };

    private async Task<bool> HandleShortcutAsync(Key key, ModifierKeys modifiers)
    {
        if (MapShortcut(key, modifiers) is not { } shortcut)
            return false;

        switch (shortcut.Command)
        {
            case ShellCommand.CommandPalette:
                OpenCommandPalette();
                break;
            case ShellCommand.SearchTabs:
                OpenCommandPalette(tabsOnly: true);
                break;
            case ShellCommand.ToggleSidebar:
                _store.Settings.ShowWorkspaceSidebar = !_store.Settings.ShowWorkspaceSidebar;
                _store.SaveSettings();
                ApplyUiPreferences();
                UpdateWorkspaceSidebar();
                break;
            case ShellCommand.FocusAddress:
                AddressBox.Focus();
                Keyboard.Focus(AddressBox);
                AddressBox.SelectAll();
                break;
            case ShellCommand.PrivateWindow:
                OpenPrivateWindow();
                break;
            case ShellCommand.ReopenClosedTab:
                if (_closedTabs.Count > 0)
                    await AddTabAsync(_closedTabs.Pop());
                break;
            case ShellCommand.NewTab:
                await AddTabAsync();
                AddressBox.Focus();
                Keyboard.Focus(AddressBox);
                break;
            case ShellCommand.CloseTab:
                if (_activeTab is not null)
                    await CloseTabAsync(_activeTab);
                break;
            case ShellCommand.NextTab:
                SelectAdjacentTab(1);
                break;
            case ShellCommand.PreviousTab:
                SelectAdjacentTab(-1);
                break;
            case ShellCommand.SelectTab:
                SelectTabByNumber(shortcut.Argument);
                break;
            case ShellCommand.TrimMemory:
                TrimMemoryNow();
                break;
            case ShellCommand.PerformanceCenter:
                ShowPerformanceCenter();
                break;
            case ShellCommand.ToggleGameMode:
                ToggleGameMode();
                if (_activeTab?.IsStartPage == true)
                    ShowStartPage(_activeTab);
                break;
            case ShellCommand.Settings:
                await OpenSettingsAsync();
                break;
            case ShellCommand.Autofill:
                await AutofillSavedPasswordAsync();
                break;
            case ShellCommand.Bookmark:
                ToggleCurrentBookmark();
                break;
            case ShellCommand.History:
                await OpenLibraryAsync("history");
                break;
            case ShellCommand.Downloads:
                await OpenLibraryAsync("downloads");
                break;
            case ShellCommand.Favorites:
                await OpenLibraryAsync("favorites");
                break;
            case ShellCommand.Reload:
            case ShellCommand.HardReload:
                await ReloadActiveTabAsync(shortcut.Command == ShellCommand.HardReload);
                break;
            case ShellCommand.ZoomIn:
                ChangeZoom(+1);
                break;
            case ShellCommand.ZoomOut:
                ChangeZoom(-1);
                break;
            case ShellCommand.ZoomReset:
                ChangeZoom(0);
                break;
            case ShellCommand.Back:
                if (_activeTab is { IsLoaded: true })
                    _activeTab.View.GoBack();
                break;
            case ShellCommand.Forward:
                if (_activeTab is { IsLoaded: true })
                    _activeTab.View.GoForward();
                break;
            case ShellCommand.Home:
                if (_activeTab is not null)
                    ShowStartPage(_activeTab);
                break;
            case ShellCommand.Fullscreen:
                ToggleFullscreen();
                break;
            case ShellCommand.DevTools:
                if (_activeTab is { IsLoaded: true })
                    _activeTab.View.ShowDevTools();
                break;
            case ShellCommand.Print:
                if (_activeTab is { IsLoaded: true, IsInternalPage: false })
                    _activeTab.View.Print();
                break;
            case ShellCommand.Find:
                OpenFindBar();
                break;
            case ShellCommand.FindNext:
                FindInPage(forward: true, findNext: true);
                break;
            case ShellCommand.FindPrevious:
                FindInPage(forward: false, findNext: true);
                break;
        }

        return true;
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            if (CommandPaletteOverlay.Visibility == Visibility.Visible)
            {
                CloseCommandPalette();
                e.Handled = true;
            }
            else if (FindBar.Visibility == Visibility.Visible && FindBar.IsKeyboardFocusWithin)
            {
                CloseFindBar();
                e.Handled = true;
            }
            else if (_videoFullscreenTab is { } videoTab)
            {
                LeaveVideoFullscreen(videoTab);
                e.Handled = true;
            }

            return;
        }

        if (!IsBrowserShortcut(key, modifiers))
            return;

        e.Handled = true;
        await HandleShortcutAsync(key, modifiers);
    }

    private async Task ReloadActiveTabAsync(bool ignoreCache)
    {
        if (_activeTab is null)
            return;

        if (!_activeTab.IsLoaded)
        {
            await EnsureTabLoadedAsync(_activeTab);
            return;
        }

        if (_activeTab.IsSettingsPage)
            ShowSettingsPage(_activeTab);
        else if (_activeTab.IsStartPage)
            ShowStartPage(_activeTab);
        else if (_activeTab.IsLibraryPage)
            ShowLibraryPage(_activeTab, _activeTab.LibrarySection);
        else if (_activeTab.IsWelcomePage)
            ShowWelcomePage(_activeTab);
        else
            _activeTab.View.Reload(ignoreCache);
    }

    private void SelectTabByNumber(int number)
    {
        List<Tabs.BrowserTab> visibleTabs = _tabs.Where(IsTabInActiveWorkspace).ToList();
        if (visibleTabs.Count == 0)
            return;

        int index = number >= 9 ? visibleTabs.Count - 1 : number - 1;
        if (index < visibleTabs.Count)
            _ = SelectTabAsync(visibleTabs[index]);
    }

    private static readonly double[] ZoomSteps = [0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0, 5.0];

    private void ChangeZoom(int direction)
    {
        if (_activeTab is not { IsLoaded: true } tab)
            return;

        double current = tab.View.ZoomFactor;
        double next = direction switch
        {
            0 => 1.0,
            > 0 => ZoomSteps.FirstOrDefault(step => step > current + 0.001, ZoomSteps[^1]),
            _ => ZoomSteps.LastOrDefault(step => step < current - 0.001, ZoomSteps[0])
        };

        tab.View.ZoomFactor = next;
        RememberSiteZoom(tab, next);
        StatusText.Text = $"Zoom {Math.Round(next * 100)}%";
    }
}
