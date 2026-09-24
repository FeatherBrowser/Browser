using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private const int MaxWorkspaces = 12;
    private const string DefaultWorkspace = "Main";

    private static bool WorkspaceEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private void NormalizeWorkspaceSettings()
    {
        _store.Settings.Workspaces ??= [];
        _store.Settings.Workspaces = _store.Settings.Workspaces
            .Where(static workspace => !string.IsNullOrWhiteSpace(workspace))
            .Select(static workspace => workspace.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxWorkspaces)
            .ToList();

        if (_store.Settings.Workspaces.Count == 0)
            _store.Settings.Workspaces.Add(DefaultWorkspace);

        if (string.IsNullOrWhiteSpace(_store.Settings.ActiveWorkspace) ||
            !_store.Settings.Workspaces.Contains(
                _store.Settings.ActiveWorkspace,
                StringComparer.OrdinalIgnoreCase))
        {
            _store.Settings.ActiveWorkspace = _store.Settings.Workspaces[0];
        }
    }

    private void EnsureWorkspaceExists(string? workspace)
    {
        NormalizeWorkspaceSettings();

        string name = string.IsNullOrWhiteSpace(workspace)
            ? DefaultWorkspace
            : workspace.Trim();

        if (_store.Settings.Workspaces.Count >= MaxWorkspaces ||
            _store.Settings.Workspaces.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _store.Settings.Workspaces.Add(name);
    }

    private bool IsTabInActiveWorkspace(BrowserTab tab) =>
        !tab.IsClosed && WorkspaceEquals(tab.Workspace, _store.Settings.ActiveWorkspace);

    private void RefreshWorkspaceTabVisibility()
    {
        foreach (BrowserTab tab in _tabs.Where(static tab => !tab.IsClosed))
        {
            tab.Header.Visibility = IsTabInActiveWorkspace(tab) ? Visibility.Visible : Visibility.Collapsed;
            }
    }

    private void UpdateWorkspaceSidebar()
    {
        if (WorkspaceButtonsPanel is null)
            return;

        NormalizeWorkspaceSettings();
        WorkspaceButtonsPanel.Children.Clear();

        foreach (string workspace in _store.Settings.Workspaces)
        {
            BrowserTab[] workspaceTabs = _tabs
                .Where(tab => !tab.IsClosed && WorkspaceEquals(tab.Workspace, workspace))
                .ToArray();

            int loadedCount = workspaceTabs.Count(static tab => tab.IsLoaded);
            bool isActive = WorkspaceEquals(workspace, _store.Settings.ActiveWorkspace);
            string suffix = BuildWorkspaceStatusSuffix(workspaceTabs.Length, loadedCount);

            Button button = CreateWorkspaceButton(workspace, suffix, isActive);
            WorkspaceButtonsPanel.Children.Add(button);
        }

        WorkspaceHintText.Text = _store.Settings.ColdUnloadOtherWorkspaces
            ? "Inactive workspaces unload"
            : "Inactive workspaces preserved";
    }

    private static string BuildWorkspaceStatusSuffix(int tabCount, int loadedCount)
    {
        if (tabCount == 0)
            return string.Empty;

        return loadedCount > 0
            ? $"  {tabCount} · {loadedCount} hot"
            : $"  {tabCount}";
    }

    private Button CreateWorkspaceButton(string workspace, string suffix, bool isActive)
    {
        var button = new Button
        {
            Content = $"{(isActive ? "●" : "○")}  {workspace}{suffix}",
            Tag = workspace,
            Style = (Style)FindResource("ChromeButton"),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontSize = 11,
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(0, 1, 0, 1),
            Foreground = isActive ? _accentBrush : (Brush)FindResource("TextPrimary"),
            ToolTip = isActive
                ? "Current workspace"
                : "Switch workspace; inactive tabs stay cold"
        };

        if (isActive)
            button.Background = (Brush)FindResource("ControlBackground");

        button.Click += async (_, _) => await SwitchWorkspaceAsync(workspace);
        button.PreviewMouseRightButtonUp += (_, e) =>
        {
            BuildWorkspaceContextMenu(workspace, button).IsOpen = true;
            e.Handled = true;
        };

        return button;
    }

    private async Task SwitchWorkspaceAsync(string workspace)
    {
        EnsureWorkspaceExists(workspace);

        _store.Settings.ActiveWorkspace = workspace;
        _store.SaveSettings();
        RefreshWorkspaceTabVisibility();

        if (_store.Settings.ColdUnloadOtherWorkspaces)
            HibernateInactiveWorkspaceTabs(workspace);

        BrowserTab? target = _tabs
            .Where(tab => !tab.IsClosed && WorkspaceEquals(tab.Workspace, workspace))
            .OrderByDescending(static tab => tab.LastActivatedUtc)
            .FirstOrDefault();

        if (target is null)
            await AddTabAsync(workspace: workspace);
        else
            await SelectTabAsync(target);

        UpdateWorkspaceSidebar();
        StatusText.Text = $"Workspace: {workspace}";
    }

    private void HibernateInactiveWorkspaceTabs(string activeWorkspace)
    {
        BrowserTab[] tabsToHibernate = _tabs
            .Where(tab =>
                !tab.IsClosed &&
                tab.IsLoaded &&
                !WorkspaceEquals(tab.Workspace, activeWorkspace))
            .OrderBy(static tab => tab.LastActivatedUtc)
            .ToArray();

        foreach (BrowserTab tab in tabsToHibernate)
            HibernateTab(tab);
    }

    private ContextMenu BuildWorkspaceContextMenu(string workspace, Button placementTarget)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.MousePoint,
            MinWidth = 190
        };

        var newTabItem = new MenuItem { Header = "New tab in this workspace" };
        newTabItem.Click += async (_, _) => await AddTabAsync(workspace: workspace);
        menu.Items.Add(newTabItem);

        var unloadItem = new MenuItem { Header = "Unload workspace tabs" };
        unloadItem.Click += (_, _) => UnloadWorkspaceTabs(workspace);
        menu.Items.Add(unloadItem);

        menu.Items.Add(new Separator());

        var renameItem = new MenuItem { Header = "Rename workspace" };
        renameItem.Click += (_, _) => RenameWorkspace(workspace);
        menu.Items.Add(renameItem);

        var deleteItem = new MenuItem
        {
            Header = "Delete workspace",
            IsEnabled = _store.Settings.Workspaces.Count > 1
        };
        deleteItem.Click += async (_, _) => await DeleteWorkspaceAsync(workspace);
        menu.Items.Add(deleteItem);

        return menu;
    }

    private void UnloadWorkspaceTabs(string workspace)
    {
        BrowserTab[] unloadableTabs = _tabs
            .Where(tab =>
                !tab.IsClosed &&
                tab.IsLoaded &&
                tab != _activeTab &&
                WorkspaceEquals(tab.Workspace, workspace))
            .ToArray();

        int unloadedCount = unloadableTabs.Sum(tab => HibernateTab(tab) ? 1 : 0);

        StatusText.Text = unloadedCount > 0
            ? $"Unloaded {unloadedCount} tab(s) in {workspace}"
            : "No workspace tabs could be unloaded";

        UpdateWorkspaceSidebar();
    }

    private void RenameWorkspace(string workspace)
    {
        string? name = PromptForWorkspaceName("Rename workspace", workspace);
        if (string.IsNullOrWhiteSpace(name) || WorkspaceEquals(name, workspace))
            return;

        if (_store.Settings.Workspaces.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            ShowWorkspaceExistsMessage();
            return;
        }

        int index = _store.Settings.Workspaces.FindIndex(item => WorkspaceEquals(item, workspace));
        if (index < 0)
            return;

        _store.Settings.Workspaces[index] = name;

        foreach (BrowserTab tab in _tabs.Where(tab => WorkspaceEquals(tab.Workspace, workspace)))
            tab.Workspace = name;

        if (WorkspaceEquals(_store.Settings.ActiveWorkspace, workspace))
            _store.Settings.ActiveWorkspace = name;

        _store.SaveSettings();
        RefreshWorkspaceTabVisibility();
        UpdateWorkspaceSidebar();
        SaveSessionSnapshot();
    }

    private async Task DeleteWorkspaceAsync(string workspace)
    {
        NormalizeWorkspaceSettings();

        if (_store.Settings.Workspaces.Count <= 1)
            return;

        MessageBoxResult result = MessageBox.Show(
            $"Delete workspace '{workspace}'? Its tabs will move to another workspace.",
            "Delete workspace",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        string destination = _store.Settings.Workspaces.First(item => !WorkspaceEquals(item, workspace));

        foreach (BrowserTab tab in _tabs.Where(tab => WorkspaceEquals(tab.Workspace, workspace)))
            tab.Workspace = destination;

        _store.Settings.Workspaces.RemoveAll(item => WorkspaceEquals(item, workspace));

        if (WorkspaceEquals(_store.Settings.ActiveWorkspace, workspace))
            _store.Settings.ActiveWorkspace = destination;

        _store.SaveSettings();
        SaveSessionSnapshot();

        await SwitchWorkspaceAsync(_store.Settings.ActiveWorkspace);
    }

    private string? PromptForWorkspaceName(string title, string initialValue)
    {
        var textBox = new TextBox
        {
            Text = initialValue,
            Margin = new Thickness(0, 8, 0, 12),
            Padding = new Thickness(8, 6, 8, 6),
            Background = (Brush)FindResource("ControlBackground"),
            Foreground = (Brush)FindResource("TextPrimary"),
            BorderBrush = _accentBrush,
            CaretBrush = (Brush)FindResource("TextPrimary")
        };

        var saveButton = new Button
        {
            Content = "Save",
            Width = 80,
            IsDefault = true,
            Style = (Style)FindResource("ChromeButton"),
            Background = (Brush)FindResource("ControlBackground")
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            IsCancel = true,
            Style = (Style)FindResource("ChromeButton"),
            Margin = new Thickness(8, 0, 0, 0)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttonPanel.Children.Add(saveButton);
        buttonPanel.Children.Add(cancelButton);

        var contentPanel = new StackPanel { Margin = new Thickness(16) };
        contentPanel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)FindResource("TextPrimary"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });
        contentPanel.Children.Add(textBox);
        contentPanel.Children.Add(buttonPanel);

        var dialog = new Window
        {
            Owner = this,
            Title = title,
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("ChromeBackground"),
            Content = contentPanel
        };

        saveButton.Click += (_, _) => dialog.DialogResult = true;

        textBox.SelectAll();
        textBox.Focus();

        return dialog.ShowDialog() == true
            ? textBox.Text.Trim()
            : null;
    }

    private void SidebarButton_Click(object sender, RoutedEventArgs e)
    {
        _store.Settings.ShowWorkspaceSidebar = !_store.Settings.ShowWorkspaceSidebar;
        _store.SaveSettings();
        ApplyUiPreferences();
        UpdateWorkspaceSidebar();
    }

    private async void AddWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        NormalizeWorkspaceSettings();

        if (_store.Settings.Workspaces.Count >= MaxWorkspaces)
        {
            MessageBox.Show(
                $"Feather supports up to {MaxWorkspaces} workspaces.",
                "Feather Browser",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string suggestedName = $"Workspace {_store.Settings.Workspaces.Count + 1}";
        string? name = PromptForWorkspaceName("New workspace", suggestedName);

        if (string.IsNullOrWhiteSpace(name))
            return;

        if (_store.Settings.Workspaces.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            ShowWorkspaceExistsMessage();
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
        if (sender is not Button { Tag: string action })
            return;

        switch (action)
        {
            case "favorites":
                await OpenLibraryAsync("favorites");
                break;

            case "history":
                await OpenLibraryAsync("history");
                break;

            case "downloads":
                await OpenLibraryAsync("downloads");
                break;

            case "settings":
                await OpenSettingsAsync();
                break;

            case "trim":
                TrimMemoryNow();
                break;
        }
    }

    private static void ShowWorkspaceExistsMessage()
    {
        MessageBox.Show(
            "A workspace with that name already exists.",
            "Feather Browser",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}