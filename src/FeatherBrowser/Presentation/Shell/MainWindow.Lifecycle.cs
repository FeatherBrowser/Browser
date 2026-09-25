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
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Presentation.Tabs;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    public async Task OpenExternalAddressAsync(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return;

        for (int attempt = 0; attempt < 100 && _environment is null && !_isClosing; attempt++)
            await Task.Delay(50);

        if (_environment is not null && !_isClosing)
            await AddTabAsync(address);
    }

    private static void CleanupStalePrivateProfiles()
    {
        try
        {
            string root = Path.Combine(Path.GetTempPath(), "FeatherBrowser", "Private");
            if (!Directory.Exists(root))
                return;

            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                try
                {
                    DateTime lastWrite = Directory.GetLastWriteTimeUtc(dir);
                    if (DateTime.UtcNow - lastWrite > TimeSpan.FromHours(24))
                        Directory.Delete(dir, true);
                }
                catch { }
            }
        }
        catch { }
    }

    private void ApplyPrivateWindowChrome()
    {
        if (!_isPrivateMode)
            return;

        TitleBar.Background = new SolidColorBrush(Color.FromRgb(24, 17, 34));
        Title = "Private — Feather Browser";
        TitleText.Text = "Private browsing";
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            string dataRoot = _isPrivateMode
                ? _privateDataRoot!
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FeatherBrowser",
                    "WebView2");
            Directory.CreateDirectory(dataRoot);

            var options = new CoreWebView2EnvironmentOptions
            {
                AreBrowserExtensionsEnabled = false
            };

            _environment = await CoreWebView2Environment.CreateAsync(
    browserExecutableFolder: null,
    userDataFolder: dataRoot);

            if (!string.IsNullOrWhiteSpace(_initialAddress))
            {
                await AddTabAsync(_initialAddress);
            }
            else
            {
                if (_isPrivateMode)
                {
                    await AddTabAsync();
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    StatusText.Text = "Private browsing · history and session are not saved";
                    _resourceTimer.Start();
                    return;
                }

                string startup = _store.Settings.StartupMode;
                if (!_store.Settings.FirstRunCompleted)
                {
                    await AddTabAsync("feather://welcome");
                }
                else if (string.Equals(startup, "Gaming", StringComparison.OrdinalIgnoreCase))
                {
                    _store.Settings.GameMode = true;
                    _store.Settings.EcoMode = true;
                    _store.Settings.LowMemoryMode = true;
                    _ecoMode = true;
                    await AddTabAsync();
                }
                else if (string.Equals(startup, "NewTab", StringComparison.OrdinalIgnoreCase) || !_store.Settings.RestorePreviousSession)
                {
                    await AddTabAsync();
                }
                else
                {
                    SessionState session = _store.LoadSessionState();
                    List<SessionTabState> states = session.TabStates.Count > 0
                        ? session.TabStates.Take(30).ToList()
                        : session.Tabs.Take(30).Select(x => new SessionTabState { Address = x, Workspace = "Main" }).ToList();

                    if (states.Count > 0)
                    {
                        int activeIndex = Math.Clamp(session.ActiveIndex, 0, states.Count - 1);
                        string activeWorkspace = string.IsNullOrWhiteSpace(states[activeIndex].Workspace) ? "Main" : states[activeIndex].Workspace;
                        EnsureWorkspaceExists(activeWorkspace);
                        _store.Settings.ActiveWorkspace = activeWorkspace;

                        var restored = new List<BrowserTab>();
                        foreach (SessionTabState state in states)
                        {
                            EnsureWorkspaceExists(state.Workspace);
                            BrowserTab? tab = await AddTabAsync(state.Address, select: false, workspace: state.Workspace, pinned: state.IsPinned, titleHint: state.Title);
                            if (tab is not null)
                                restored.Add(tab);
                        }

                        UpdateWorkspaceSidebar();
                        if (restored.Count > 0)
                            await SelectTabAsync(restored[Math.Clamp(activeIndex, 0, restored.Count - 1)]);
                        else
                            await AddTabAsync();
                    }
                    else
                    {
                        await AddTabAsync();
                    }
                }
            }

            LoadingOverlay.Visibility = Visibility.Collapsed;
            StatusText.Text = _store.Settings.GameMode
                ? "Gaming Mode ready · active tab hot, background tabs cold"
                : _store.Settings.LowMemoryMode
                    ? $"Ready · max {_store.Settings.MaxLoadedTabs} loaded tab(s), cold after {_store.Settings.UnloadAfterSeconds}s"
                    : $"Ready · hidden tabs suspend after {_store.Settings.SleepAfterSeconds}s";
            _resourceTimer.Start();
            _sessionTimer.Start();
            SaveSessionSnapshot();
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            StatusText.Text = "WebView2 failed to start";
            MessageBox.Show(
                $"Feather could not start WebView2.\n\n{ex.Message}\n\nInstall or repair the Microsoft Edge WebView2 Runtime and try again.",
                "Feather Browser",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string PrepareWebAssets()
    {
        string assetsRoot = Path.Join(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "FeatherBrowser",
            "Assets");

        Directory.CreateDirectory(assetsRoot);

        string backgroundPath =
            Path.Join(assetsRoot, "background.png");

        if (!File.Exists(backgroundPath))
        {
            File.WriteAllBytes(
                backgroundPath,
                FeatherBrowser.Infrastructure.Resources.EmbeddedAssets
                    .LoadBytes("background.png"));
        }

        return assetsRoot;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _isClosing = true;
        _resourceTimer.Stop();
        _sessionTimer.Stop();
        if (!_isPrivateMode)
            SaveSessionSnapshot();

        foreach (BrowserTab tab in _tabs.ToArray())
        {
            CancelSleepSchedule(tab);
            tab.View.Dispose();
        }
        _tabs.Clear();

        if (_isPrivateMode && !string.IsNullOrWhiteSpace(_privateDataRoot))
        {
            try { Directory.Delete(_privateDataRoot, true); }
            catch { }
        }
    }

    private string SessionAddress(BrowserTab tab) =>
        tab.IsSettingsPage ? "feather://settings"
        : tab.IsLibraryPage ? $"feather://{tab.LibrarySection}"
        : tab.IsStartPage ? "feather://newtab"
        : string.IsNullOrWhiteSpace(tab.LastAddress) ? "feather://newtab" : tab.LastAddress;

    private string? _lastSessionSnapshot;

    private void SaveSessionSnapshot()
    {
        if (_isPrivateMode)
            return;
        if (_tabs.Count == 0)
            return;

        List<BrowserTab> liveTabs = _tabs.Where(t => !t.IsClosed).Take(40).ToList();
        if (liveTabs.Count == 0)
            return;

        int activeIndex = _activeTab is null ? 0 : Math.Max(0, liveTabs.IndexOf(_activeTab));
        var states = liveTabs.Select(t => new SessionTabState
        {
            Address = SessionAddress(t),
            Title = t.Title.Text,
            Workspace = t.Workspace,
            IsPinned = t.IsPinned
        }).ToList();
        string fingerprint = JsonSerializer.Serialize(new { states, activeIndex });
        if (fingerprint == _lastSessionSnapshot) return;
        if (_store.SaveSessionState(states, activeIndex))
            _lastSessionSnapshot = fingerprint;
    }
}

