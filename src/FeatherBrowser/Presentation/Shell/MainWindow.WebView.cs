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
using FeatherBrowser.Features.Blocking;
using FeatherBrowser.Presentation.Pages;
using FeatherBrowser.Presentation.Tabs;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private WebView2 CreateWebViewControl() => new()
    {
        Visibility = Visibility.Collapsed,
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 14, 16, 20)
    };

    private Task EnsureTabLoadedAsync(BrowserTab tab)
    {
        if (tab.IsClosed || tab.IsLoaded || _environment is null) return Task.CompletedTask;
        if (tab.LoadingTask is { IsCompleted: false }) return tab.LoadingTask;
        tab.LoadingTask = LoadTabCoreAsync(tab);
        return tab.LoadingTask;
    }

    private async Task LoadTabCoreAsync(BrowserTab tab)
    {
        var view = tab.View;
        BrowserHost.Children.Insert(Math.Max(0, BrowserHost.Children.Count - 1), view);
        try
        {
            await view.EnsureCoreWebView2Async(_environment);
            if (tab.IsClosed || _isClosing || tab.View != view) return;
            tab.IsLoaded = true;
            tab.NeedsContentRestore = false;
            ConfigureWebView(tab);
            RestoreTabContent(tab);
            tab.UpdateStateIndicator();
        }
        catch
        {
            BrowserHost.Children.Remove(view);
            view.Dispose();
            if (!tab.IsClosed && tab.View == view)
            {
                tab.View = CreateWebViewControl();
                tab.IsLoaded = false;
                tab.NeedsContentRestore = true;
                tab.UpdateStateIndicator();
            }
            throw;
        }
    }

    private void RestoreTabContent(BrowserTab tab)
    {
        if (!tab.IsLoaded)
            return;

        if (tab.IsWelcomePage)
        {
            tab.View.NavigateToString(WelcomePage.Html());
            return;
        }

        if (tab.IsSettingsPage)
        {
            tab.SettingsSection = SettingsPage.NormalizeSection(tab.SettingsSection);

            tab.View.NavigateToString(
                SettingsPage.Html(
                    _store.Settings,
                    _store.Bookmarks.Count,
                    _store.History.Count,
                    _passwordVault.Count,
                    _accountPageState,
                    tab.SettingsSection));

            return;
        }

        if (tab.IsLibraryPage)
        {
            tab.View.NavigateToString(LibraryPage.Html(_store.Bookmarks, _store.History, _store.Downloads, tab.LibrarySection));
            return;
        }

        if (tab.IsStartPage || string.IsNullOrWhiteSpace(tab.LastAddress))
        {
            (string name, string prefix) = SearchEngineInfo();
            tab.View.NavigateToString(StartPage.Html(name, prefix, _store.Settings));
            return;
        }

        tab.View.CoreWebView2.Navigate(tab.LastAddress);
    }

    private async Task HandleDeferredWebMessageAsync(BrowserTab tab, string messageJson)
    {
        try
        {
            await HandleWebMessageAsync(tab, messageJson);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Deferred WebView message failed: {exception}");
            StatusText.Text = "The requested action could not be completed";
        }
    }

    private void ConfigureWebView(BrowserTab tab)
    {
        CoreWebView2 core = tab.View.CoreWebView2;

        core.ContainsFullScreenElementChanged += (_, _) =>
        {
            if (_isClosing || tab.IsClosed || !tab.IsLoaded ||
                !ReferenceEquals(tab.View.CoreWebView2, core))
                return;

            if (core.ContainsFullScreenElement)
            {
                if (!ReferenceEquals(tab, _activeTab))
                {
                    _ = ExitDocumentFullscreenAsync(tab);
                    return;
                }

                _videoFullscreenTab = tab;
            }
            else if (ReferenceEquals(_videoFullscreenTab, tab))
            {
                _videoFullscreenTab = null;
            }

            ApplyFullscreen();
        };

        core.NavigationStarting += (_, _) =>
        {
            if (ReferenceEquals(_videoFullscreenTab, tab))
            {
                _videoFullscreenTab = null;
                ApplyFullscreen();
            }
        };

        string assetsRoot = PrepareWebAssets();

        core.SetVirtualHostNameToFolderMapping(
            "feather-assets",
            assetsRoot,
            CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.IsBuiltInErrorPageEnabled = true;
        core.Settings.IsWebMessageEnabled = true;
        core.Profile.IsGeneralAutofillEnabled = !_isPrivateMode;
        core.Profile.IsPasswordAutosaveEnabled = !_isPrivateMode;

        core.PermissionRequested += (_, e) => Dispatcher.Invoke(() =>
        {
            string host = SafeHost(core.Source ?? tab.LastAddress).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(host) || host == "page")
                return;

            string kind = e.PermissionKind.ToString();
            string key = PermissionKey(host, kind);
            if (_store.Settings.SitePermissions.TryGetValue(key, out string? decision))
            {
                e.State = decision switch
                {
                    "Allow" => CoreWebView2PermissionState.Allow,
                    "Block" => CoreWebView2PermissionState.Deny,
                    _ => CoreWebView2PermissionState.Default
                };
                return;
            }

            if (_store.Settings.BlockNotificationPrompts && string.Equals(kind, "Notifications", StringComparison.OrdinalIgnoreCase))
                e.State = CoreWebView2PermissionState.Deny;
        });

        core.DocumentTitleChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if (tab.IsClosed)
                return;

            string value = core.DocumentTitle;
            tab.Title.Text = string.IsNullOrWhiteSpace(value) ? "New Tab" : value;
            if (_activeTab == tab)
            {
                Title = $"{tab.Title.Text} — Feather";
                TitleText.Text = tab.Title.Text;
                UpdateBookmarkButton();
            }
        });

        core.SourceChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if (tab.IsClosed)
                return;

            string source = core.Source ?? string.Empty;
            if (!tab.IsInternalPage)
            {
                tab.LastAddress = source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || source == "about:blank"
                    ? string.Empty
                    : source;
            }

            if (_activeTab == tab)
            {
                AddressBox.Text = DisplayAddress(tab);
                UpdateBookmarkButton();
            }
        });

        core.HistoryChanged += (_, _) => Dispatcher.Invoke(UpdateNavigationButtons);

        core.IsDocumentPlayingAudioChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if (tab.IsClosed || !tab.IsLoaded)
                return;
            tab.IsPlayingAudio = core.IsDocumentPlayingAudio;
            tab.UpdateStateIndicator();
            if (!tab.IsPlayingAudio && tab != _activeTab)
            {
                ScheduleBackgroundLifecycle(tab);
            }
        });

        core.NavigationStarting += (_, e) => Dispatcher.Invoke(() =>
        {
            if (_store.Settings.StripTrackingParameters && !tab.IsInternalPage && !string.IsNullOrWhiteSpace(e.Uri))
            {
                string cleaned = _blocker.CleanTopLevelUrl(e.Uri);
                if (!string.Equals(cleaned, e.Uri, StringComparison.Ordinal))
                {
                    e.Cancel = true;
                    tab.LastAddress = cleaned;
                    core.Navigate(cleaned);
                    return;
                }
            }

            if (_activeTab == tab)
            {
                StatusText.Text = $"Loading {SafeHost(e.Uri)}…";
                NavigationProgress.Visibility = Visibility.Visible;
            }
        });

        core.NavigationCompleted += async (_, e) =>
        {
            if (e.IsSuccess && !tab.IsInternalPage)
            {
                string source = core.Source ?? tab.LastAddress;
                tab.LastAddress = source;
                if (!_isPrivateMode)
                {
                    _store.AddHistory(core.DocumentTitle, source);
                    if (_sessionTimer.IsEnabled)
                        SaveSessionSnapshot();
                }

                if (_shieldEnabled && _store.Settings.CosmeticBlocking && !_blocker.IsSiteAllowlisted(SafeHost(source), _store.Settings))
                {
                    try
                    {
                        await core.ExecuteScriptAsync(_blocker.GetCosmeticFilterScript(core.Source ?? tab.LastAddress));
                    }
                    catch (ObjectDisposedException ex)
                    {
                        System.Diagnostics.Trace.TraceWarning(
                            $"Cosmetic filtering skipped: WebView was disposed. {ex.Message}");
                    }
                    catch (InvalidOperationException ex)
                    {
                        System.Diagnostics.Trace.TraceWarning(
                            $"Cosmetic filtering failed: WebView is unavailable. {ex.Message}");
                    }
                }
            }

            if (_activeTab == tab)
            {
                NavigationProgress.Visibility = Visibility.Collapsed;
                StatusText.Text = e.IsSuccess ? "Done" : $"Navigation error: {e.WebErrorStatus}";
                AddressBox.Text = DisplayAddress(tab);
                UpdateNavigationButtons();
                UpdateBookmarkButton();
                UpdateFeatureButtons();
            }
        };

        core.NewWindowRequested += async (_, e) =>
        {
            e.Handled = true;
            await AddTabAsync(e.Uri);
        };

        core.DownloadStarting += (_, e) => Dispatcher.Invoke(() =>
        {
            tab.HasActiveDownload = true;
            string file = Path.GetFileName(e.ResultFilePath);
            string sourceUrl = core.Source ?? tab.LastAddress;
            DownloadEntry? download = _isPrivateMode ? null : _store.AddDownload(e.ResultFilePath, sourceUrl);
            StatusText.Text = string.IsNullOrWhiteSpace(file) ? "Download started" : $"Downloading {file}";

            e.DownloadOperation.StateChanged += (_, _) => Dispatcher.Invoke(() =>
            {
                CoreWebView2DownloadState state = e.DownloadOperation.State;
                if (state is CoreWebView2DownloadState.Completed or CoreWebView2DownloadState.Interrupted)
                {
                    tab.HasActiveDownload = false;
                    string label = state == CoreWebView2DownloadState.Completed ? "Completed" : "Interrupted";
                    if (download is not null)
                    {
                        _store.UpdateDownload(download.Id, label, e.ResultFilePath);
                        if (_activeTab?.IsLibraryPage == true && _activeTab.LibrarySection == "downloads")
                            ShowLibraryPage(_activeTab, "downloads");
                    }
                    if (tab != _activeTab)
                        ScheduleBackgroundLifecycle(tab);
                }
            });
        });

        core.ProcessFailed += (_, e) => Dispatcher.BeginInvoke(new Action(async () =>
        {
            await RecoverFailedTabAsync(tab, e);
        }));

        core.WebMessageReceived += (_, e) =>
        {
            string messageJson;

            try
            {
                messageJson = e.WebMessageAsJson;
            }
            catch (InvalidOperationException)
            {
                return;
            }
            catch (COMException)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => _ = HandleDeferredWebMessageAsync(tab, messageJson)));
        };

        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (_store.Settings.SendDoNotTrack)
            {
                e.Request.Headers.SetHeader("DNT", "1");
            }

            if (!_shieldEnabled || _environment is null)
                return;

            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out Uri? uri) ||
                !_blocker.ShouldBlock(uri, core.Source ?? tab.LastAddress, e.ResourceContext, _store.Settings))
                return;

            e.Response = _environment.CreateWebResourceResponse(
                Stream.Null,
                403,
                "Blocked by Feather Shield",
                "Content-Type: text/plain\r\nCache-Control: no-store\r\n");

            tab.BlockedRequests++;
            if (_activeTab == tab)
                Dispatcher.BeginInvoke(new Action(UpdateFeatureButtons));
        };
    }

    private async Task RecoverFailedTabAsync(BrowserTab tab, CoreWebView2ProcessFailedEventArgs args)
    {
        if (tab.IsClosed || _isClosing)
            return;

        string kind = args.ProcessFailedKind.ToString();
        bool rendererFailure = kind.Contains("Render", StringComparison.OrdinalIgnoreCase) ||
                               kind.Contains("Frame", StringComparison.OrdinalIgnoreCase);
        if (!rendererFailure)
        {
            if (_activeTab == tab)
            {
                NavigationProgress.Visibility = Visibility.Collapsed;
                StatusText.Text = $"WebView2 process issue: {kind}";
            }
            return;
        }

        if (ReferenceEquals(_videoFullscreenTab, tab))
        {
            _videoFullscreenTab = null;
            ApplyFullscreen();
        }

        bool wasActive = _activeTab == tab;
        if (!tab.IsInternalPage &&
     tab.IsLoaded &&
     tab.View.CoreWebView2 is { } core)
        {
            string source = core.Source ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(source) &&
                !source.Equals("about:blank", StringComparison.OrdinalIgnoreCase) &&
                !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                tab.LastAddress = source;
            }
        }

        CancelSleepSchedule(tab);
        if (tab.IsLoaded)
            BrowserHost.Children.Remove(tab.View);
        try
        {
            tab.View.Dispose();
        }
        catch (ObjectDisposedException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"WebView2 was already disposed: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"WebView2 disposal failed: {ex.Message}");
        }
        tab.View = CreateWebViewControl();
        tab.IsLoaded = false;
        tab.IsPlayingAudio = false;
        tab.NeedsContentRestore = true;
        tab.UpdateStateIndicator();

        if (!wasActive)
        {
            StatusText.Text = "A crashed background tab was unloaded safely";
            return;
        }

        NavigationProgress.Visibility = Visibility.Collapsed;
        StatusText.Text = "Tab renderer crashed · recovering…";
        try
        {
            await EnsureTabLoadedAsync(tab);
            tab.View.Visibility = Visibility.Visible;
            Panel.SetZIndex(tab.View, 10);
            AddressBox.Text = DisplayAddress(tab);
            StatusText.Text = "Tab recovered";
        }
        catch (OperationCanceledException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Tab recovery was cancelled: {ex.Message}");
            StatusText.Text = "Tab recovery cancelled · press Ctrl+R to retry";
        }
        catch (ObjectDisposedException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Tab recovery failed because the WebView was disposed: {ex.Message}");
            StatusText.Text = "Tab recovery failed · press Ctrl+R to retry";
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Tab recovery failed due to an invalid WebView state: {ex.Message}");
            StatusText.Text = "Tab recovery failed · press Ctrl+R to retry";
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Tab recovery failed due to a WebView2 COM error: {ex.Message}");
            StatusText.Text = "Tab recovery failed · press Ctrl+R to retry";
        }
        UpdateResourceText();
    }
}


