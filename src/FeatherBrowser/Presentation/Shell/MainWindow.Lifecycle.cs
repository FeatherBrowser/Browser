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
using FeatherBrowser.Presentation.Themes;
using FeatherBrowser.Features.Engine;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    public async Task OpenExternalAddressAsync(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return;
        await _startupCompleted.Task;

        if (_isClosing)
            return;

        if (_engineReady)
            await AddTabAsync(address);
        else
            StatusText.Text = $"Could not open {SafeHost(address)}: the browser engine failed to start";
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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"Could not remove old private profile {dir}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not scan old private profiles: {ex.Message}");
        }
    }

    private void ApplyPrivateWindowChrome()
    {
        if (!_isPrivateMode)
            return;

        Resources["ChromeBrush"] = ThemeManager.Brush("PrivateChromeBrush");
        Resources["TabActiveBrush"] = ThemeManager.Brush("PrivateTabActiveBrush");
        Title = "Private — Feather";
        TitleText.Text = "Private browsing";
    }

    private async Task ReportRuleLoadFailureAsync()
    {
        try
        {
            await _rulesLoading;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Feather Shield rules failed to load: {ex.Message}");
            StatusText.Text = "Feather Shield filter lists could not be loaded";
        }
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            await FeatherEngine.WhenReady;
            if (_isPrivateMode)
                _privateContext = FeatherEngine.CreatePrivateRequestContext();

            _ = ReportRuleLoadFailureAsync();
            _engineReady = true;

            string? initialAddress = string.IsNullOrWhiteSpace(_initialAddress) ? null : _initialAddress;

            if (_isPrivateMode)
            {
                await AddTabAsync(initialAddress);
                LoadingOverlay.Visibility = Visibility.Collapsed;
                StatusText.Text = "Private browsing · history and session are not saved";
                _resourceTimer.Start();
                return;
            }

            string startup = _store.Settings.StartupMode;
            if (!_store.Settings.FirstRunCompleted)
            {
                await AddTabAsync(initialAddress ?? "feather://welcome");
            }
            else if (string.Equals(startup, "Gaming", StringComparison.OrdinalIgnoreCase))
            {
                _store.Settings.GameMode = true;
                _store.Settings.EcoMode = true;
                _store.Settings.LowMemoryMode = true;
                _ecoMode = true;
                await AddTabAsync(initialAddress);
            }
            else if (string.Equals(startup, "NewTab", StringComparison.OrdinalIgnoreCase) || !_store.Settings.RestorePreviousSession)
            {
                await AddTabAsync(initialAddress);
            }
            else
            {
                SessionState session = _store.LoadSessionState();
                List<SessionTabState> states = session.TabStates.Count > 0
                    ? session.TabStates.ToList()
                    : session.Tabs.Select(x => new SessionTabState { Address = x, Workspace = "Main" }).ToList();

                var restored = new List<BrowserTab>();
                int activeIndex = 0;
                if (states.Count > 0)
                {
                    activeIndex = Math.Clamp(session.ActiveIndex, 0, states.Count - 1);
                    string activeWorkspace = string.IsNullOrWhiteSpace(states[activeIndex].Workspace) ? "Main" : states[activeIndex].Workspace;
                    EnsureWorkspaceExists(activeWorkspace);
                    _store.Settings.ActiveWorkspace = activeWorkspace;

                    foreach (SessionTabState state in states)
                    {
                        EnsureWorkspaceExists(state.Workspace);
                        BrowserTab? tab = await AddTabAsync(state.Address, select: false, workspace: state.Workspace, pinned: state.IsPinned, titleHint: state.Title);
                        if (tab is not null)
                            restored.Add(tab);
                    }

                    UpdateWorkspaceSidebar();
                }

                if (initialAddress is not null)
                    await AddTabAsync(initialAddress);
                else if (restored.Count > 0)
                    await SelectTabAsync(restored[Math.Clamp(activeIndex, 0, restored.Count - 1)]);
                else
                    await AddTabAsync();
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
            StatusText.Text = "The browser engine failed to start";
            MessageBox.Show(
                $"Feather's browser engine could not start.\n\n{ex.Message}\n\nTry reinstalling Feather. If it keeps happening, delete the Chromium folder under %LOCALAPPDATA%\\FeatherBrowser.",
                "Feather Browser",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _startupCompleted.TrySetResult();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _isClosing = true;
        _startupCompleted.TrySetResult();
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
        _store.SettingsSaved -= OnSettingsSaved;
        _store.FlushDeferredSaves();
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

