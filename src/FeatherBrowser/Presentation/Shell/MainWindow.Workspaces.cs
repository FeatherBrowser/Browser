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
    private void NormalizeWorkspaceSettings()
    {
        _store.Settings.Workspaces ??= [];
        _store.Settings.Workspaces = _store.Settings.Workspaces
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        if (_store.Settings.Workspaces.Count == 0)
            _store.Settings.Workspaces.Add("Main");

        if (string.IsNullOrWhiteSpace(_store.Settings.ActiveWorkspace) ||
            !_store.Settings.Workspaces.Contains(_store.Settings.ActiveWorkspace, StringComparer.OrdinalIgnoreCase))
            _store.Settings.ActiveWorkspace = _store.Settings.Workspaces[0];
    }

    private void EnsureWorkspaceExists(string? workspace)
    {
        string name = string.IsNullOrWhiteSpace(workspace) ? "Main" : workspace.Trim();
        if (!_store.Settings.Workspaces.Contains(name, StringComparer.OrdinalIgnoreCase) && _store.Settings.Workspaces.Count < 12)
            _store.Settings.Workspaces.Add(name);
    }

    private bool IsTabInActiveWorkspace(BrowserTab tab) =>
        !tab.IsClosed && string.Equals(tab.Workspace, _store.Settings.ActiveWorkspace, StringComparison.OrdinalIgnoreCase);

    private void RefreshWorkspaceTabVisibility()
    {
        foreach (BrowserTab tab in _tabs.Where(t => !t.IsClosed))
            tab.Header.Visibility = IsTabInActiveWorkspace(tab) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateWorkspaceSidebar()
    {
        if (WorkspaceButtonsPanel is null)
            return;

        NormalizeWorkspaceSettings();
        WorkspaceButtonsPanel.Children.Clear();
        foreach (string workspace in _store.Settings.Workspaces.ToList())
        {
            int count = _tabs.Count(t => !t.IsClosed && string.Equals(t.Workspace, workspace, StringComparison.OrdinalIgnoreCase));
            int loaded = _tabs.Count(t => !t.IsClosed && t.IsLoaded && string.Equals(t.Workspace, workspace, StringComparison.OrdinalIgnoreCase));
            bool active = string.Equals(workspace, _store.Settings.ActiveWorkspace, StringComparison.OrdinalIgnoreCase);
            string suffix = count == 0 ? string.Empty : $"  {count}";
            if (loaded > 0)
                suffix += $" · {loaded} hot";

            var button = new Button
            {
                Content = $"{(active ? "●" : "○")}  {workspace}{suffix}",
                Tag = workspace,
                Style = (Style)FindResource("ChromeButton"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 11,
                Padding = new Thickness(8, 7, 8, 7),
                Margin = new Thickness(0, 1, 0, 1),
                Foreground = active ? _accentBrush : (Brush)FindResource("TextPrimary"),
                ToolTip = active ? "Current workspace" : "Switch workspace; inactive tabs stay cold"
            };
            if (active)
                button.Background = (Brush)FindResource("ControlBackground");

            button.Click += async (_, _) => await SwitchWorkspaceAsync(workspace);
            button.PreviewMouseRightButtonUp += (_, e) =>
            {
                BuildWorkspaceContextMenu(workspace, button).IsOpen = true;
                e.Handled = true;
            };
            WorkspaceButtonsPanel.Children.Add(button);
        }

        WorkspaceHintText.Text = _store.Settings.ColdUnloadOtherWorkspaces ? "Inactive workspaces unload" : "Inactive workspaces preserved";
    }

    private async Task SwitchWorkspaceAsync(string workspace)
    {
        EnsureWorkspaceExists(workspace);
        _store.Settings.ActiveWorkspace = workspace;
        _store.SaveSettings();
        RefreshWorkspaceTabVisibility();

        if (_store.Settings.ColdUnloadOtherWorkspaces)
        {
            foreach (BrowserTab other in _tabs
                         .Where(t => !t.IsClosed && t.IsLoaded && !string.Equals(t.Workspace, workspace, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(t => t.LastActivatedUtc)
                         .ToList())
                HibernateTab(other);
        }

        BrowserTab? target = _tabs
            .Where(t => !t.IsClosed && string.Equals(t.Workspace, workspace, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.LastActivatedUtc)
            .FirstOrDefault();

        if (target is null)
            await AddTabAsync(workspace: workspace);
        else
            await SelectTabAsync(target);

        UpdateWorkspaceSidebar();
        StatusText.Text = $"Workspace: {workspace}";
    }

    private ContextMenu BuildWorkspaceContextMenu(string workspace, Button placementTarget)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget, Placement = PlacementMode.MousePoint, MinWidth = 190 };
        var newTab = new MenuItem { Header = "New tab in this workspace" };
        newTab.Click += async (_, _) => await AddTabAsync(workspace: workspace);
        menu.Items.Add(newTab);

        var unload = new MenuItem { Header = "Unload workspace tabs" };
        unload.Click += (_, _) =>
        {
            int count = 0;
            foreach (BrowserTab tab in _tabs.Where(t => !t.IsClosed && t.IsLoaded && string.Equals(t.Workspace, workspace, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                if (tab == _activeTab)
                    continue;
                if (HibernateTab(tab))
                    count++;
            }
            StatusText.Text = count > 0 ? $"Unloaded {count} tab(s) in {workspace}" : "No workspace tabs could be unloaded";
            UpdateWorkspaceSidebar();
        };
        menu.Items.Add(unload);
        menu.Items.Add(new Separator());

        var rename = new MenuItem { Header = "Rename workspace" };
        rename.Click += async (_, _) =>
        {
            string? name = PromptForWorkspaceName("Rename workspace", workspace);
            if (string.IsNullOrWhiteSpace(name) || string.Equals(name, workspace, StringComparison.OrdinalIgnoreCase))
                return;
            if (_store.Settings.Workspaces.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show("A workspace with that name already exists.", "Feather Browser", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            int index = _store.Settings.Workspaces.FindIndex(x => string.Equals(x, workspace, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                _store.Settings.Workspaces[index] = name;
            foreach (BrowserTab tab in _tabs.Where(t => string.Equals(t.Workspace, workspace, StringComparison.OrdinalIgnoreCase)))
                tab.Workspace = name;
            if (string.Equals(_store.Settings.ActiveWorkspace, workspace, StringComparison.OrdinalIgnoreCase))
                _store.Settings.ActiveWorkspace = name;
            _store.SaveSettings();
            RefreshWorkspaceTabVisibility();
            UpdateWorkspaceSidebar();
            SaveSessionSnapshot();
            await Task.CompletedTask;
        };
        menu.Items.Add(rename);

        var delete = new MenuItem { Header = "Delete workspace", IsEnabled = _store.Settings.Workspaces.Count > 1 };
        delete.Click += async (_, _) => await DeleteWorkspaceAsync(workspace);
        menu.Items.Add(delete);
        return menu;
    }

    private async Task DeleteWorkspaceAsync(string workspace)
    {
        if (_store.Settings.Workspaces.Count <= 1)
            return;
        if (MessageBox.Show($"Delete workspace '{workspace}'? Its tabs will move to another workspace.", "Delete workspace", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        string destination = _store.Settings.Workspaces.First(x => !string.Equals(x, workspace, StringComparison.OrdinalIgnoreCase));
        foreach (BrowserTab tab in _tabs.Where(t => string.Equals(t.Workspace, workspace, StringComparison.OrdinalIgnoreCase)))
            tab.Workspace = destination;
        _store.Settings.Workspaces.RemoveAll(x => string.Equals(x, workspace, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(_store.Settings.ActiveWorkspace, workspace, StringComparison.OrdinalIgnoreCase))
            _store.Settings.ActiveWorkspace = destination;
        _store.SaveSettings();
        RefreshWorkspaceTabVisibility();
        UpdateWorkspaceSidebar();
        SaveSessionSnapshot();
        await SwitchWorkspaceAsync(_store.Settings.ActiveWorkspace);
    }

    private string? PromptForWorkspaceName(string title, string initialValue)
    {
        var box = new TextBox
        {
            Text = initialValue,
            Margin = new Thickness(0, 8, 0, 12),
            Padding = new Thickness(8, 6, 8, 6),
            Background = (Brush)FindResource("ControlBackground"),
            Foreground = (Brush)FindResource("TextPrimary"),
            BorderBrush = _accentBrush,
            CaretBrush = (Brush)FindResource("TextPrimary")
        };
        var ok = new Button { Content = "Save", Width = 80, IsDefault = true, Style = (Style)FindResource("ChromeButton"), Background = (Brush)FindResource("ControlBackground") };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true, Style = (Style)FindResource("ChromeButton"), Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = title, Foreground = (Brush)FindResource("TextPrimary"), FontSize = 16, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var dialog = new Window
        {
            Owner = this,
            Title = title,
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("ChromeBackground"),
            Content = panel
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        box.SelectAll();
        box.Focus();
        bool? result = dialog.ShowDialog();
        return result == true ? box.Text.Trim() : null;
    }

    private async void SidebarButton_Click(object sender, RoutedEventArgs e)
    {
        _store.Settings.ShowWorkspaceSidebar = !_store.Settings.ShowWorkspaceSidebar;
        _store.SaveSettings();
        ApplyUiPreferences();
        UpdateWorkspaceSidebar();
        await Task.CompletedTask;
    }

    private async void AddWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_store.Settings.Workspaces.Count >= 12)
        {
            MessageBox.Show("Feather supports up to 12 workspaces.", "Feather Browser", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string suggested = $"Workspace {_store.Settings.Workspaces.Count + 1}";
        string? name = PromptForWorkspaceName("New workspace", suggested);
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (_store.Settings.Workspaces.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show("A workspace with that name already exists.", "Feather Browser", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _store.Settings.Workspaces.Add(name);
        _store.Settings.ActiveWorkspace = name;
        _store.SaveSettings();
        UpdateWorkspaceSidebar();
        RefreshWorkspaceTabVisibility();
        await AddTabAsync(workspace: name);
    }

    private async void SidebarQuickButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string action)
            return;
        switch (action)
        {
            case "favorites": await OpenLibraryAsync("favorites"); break;
            case "history": await OpenLibraryAsync("history"); break;
            case "downloads": await OpenLibraryAsync("downloads"); break;
            case "settings": await OpenSettingsAsync(); break;
            case "trim": TrimMemoryNow(); break;
        }
    }
}
