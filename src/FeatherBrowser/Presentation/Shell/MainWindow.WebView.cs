using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FeatherBrowser.Domain.Models;
using FeatherBrowser.Features.Engine;
using FeatherBrowser.Features.Security;
using FeatherBrowser.Presentation.Pages;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private readonly Dictionary<(WebPage Page, int Id), DownloadEntry?> _downloadEntries = [];
    private string? _statusBeforeHover;

    private readonly System.Windows.Controls.TextBlock StatusText = new();

    private WebPage CreateWebViewControl() => new(new WebPageOptions
    {
        Network = _blocker,
        RequestContext = _privateContext,
        IsShortcut = IsBrowserShortcut,
        IsReservedShortcut = IsReservedBrowserShortcut,
        DownloadFolder = DownloadPaths.DefaultFolder,
        NavigationGate = _rulesGate,
    });

    private WebPage CreatePageFor(BrowserTab tab)
    {
        WebPage page = CreateWebViewControl();
        HookPage(tab, page);
        return page;
    }

    private Task EnsureTabLoadedAsync(BrowserTab tab)
    {
        if (tab.IsClosed || tab.IsLoaded || !_engineReady)
            return Task.CompletedTask;
        if (tab.LoadingTask is { IsCompleted: false })
            return tab.LoadingTask;
        tab.LoadingTask = LoadTabCoreAsync(tab);
        return tab.LoadingTask;
    }

    private async Task LoadTabCoreAsync(BrowserTab tab)
    {
        WebPage page = tab.View;

        if (_activeTab == tab && CommandPaletteOverlay.Visibility != Visibility.Visible && WindowState != WindowState.Minimized)
            page.SetVisible(true, zIndex: 10);

        FrameworkElement control = page.Control;
        if (!BrowserHost.Children.Contains(control))
            BrowserHost.Children.Insert(Math.Max(0, BrowserHost.Children.Count - 1), control);

        try
        {
            await page.EnsureCreatedAsync();
            if (tab.IsClosed || _isClosing || tab.View != page)
                return;

            tab.IsLoaded = true;
            tab.NeedsContentRestore = false;
            RestoreTabContent(tab);
            tab.UpdateStateIndicator();
        }
        catch
        {
            RemovePageControl(page);
            page.Dispose();
            if (!tab.IsClosed && tab.View == page)
            {
                tab.View = CreatePageFor(tab);
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
            tab.View.NavigateToInternalPage("welcome", PrepareInternalPageHtml(tab, WelcomePage.Html()));
            return;
        }

        if (tab.IsSettingsPage)
        {
            tab.SettingsSection = SettingsPage.NormalizeSection(tab.SettingsSection);
            tab.View.NavigateToInternalPage(
                "settings",
                PrepareInternalPageHtml(
                    tab,
                    SettingsPage.Html(
                        _store.Settings,
                        _store.Bookmarks.Count,
                        _store.History.Count,
                        _passwordVault.Count,
                        _accountPageState,
                        tab.SettingsSection)));
            return;
        }

        if (tab.IsLibraryPage)
        {
            tab.View.NavigateToInternalPage(
                tab.LibrarySection,
                PrepareInternalPageHtml(tab, LibraryPage.Html(_store.Bookmarks, _store.History, _store.Downloads, tab.LibrarySection)));
            return;
        }

        if (tab.IsStartPage || string.IsNullOrWhiteSpace(tab.LastAddress))
        {
            ShowStartPageContent(tab);
            return;
        }

        tab.View.Navigate(tab.LastAddress);
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
            Debug.WriteLine($"Deferred page message failed: {exception}");
            StatusText.Text = "The requested action could not be completed";
        }
    }

    private void ForgetPageDownloads(BrowserTab tab, WebPage page)
    {
        foreach (var key in _downloadEntries.Keys.Where(key => ReferenceEquals(key.Page, page)).ToList())
        {
            if (_downloadEntries[key] is { } entry)
                _store.UpdateDownload(entry.Id, "Interrupted", entry.FilePath);
            _downloadEntries.Remove(key);
        }

        tab.ResetDownloads();
    }

    private void RemovePageControl(WebPage page)
    {
        if (page.HasControl)
            BrowserHost.Children.Remove(page.Control);
    }

    private bool IsCurrent(BrowserTab tab, WebPage page) =>
        !_isClosing && !tab.IsClosed && ReferenceEquals(tab.View, page);

    private void HookPage(BrowserTab tab, WebPage page)
    {
        page.FullscreenChanged += (_, fullscreen) =>
        {
            if (!IsCurrent(tab, page) || !tab.IsLoaded)
                return;

            if (fullscreen)
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

        page.NavigationStarting += (_, url) =>
        {
            if (!IsCurrent(tab, page))
                return;

            if (ReferenceEquals(_videoFullscreenTab, tab))
            {
                _videoFullscreenTab = null;
                ApplyFullscreen();
            }

            bool internalDocument = InternalPageMessagePolicy.IsInternalDocumentSource(url);
            if (tab.IsInternalPage && !internalDocument)
            {
                ClearInternalPageState(tab);
                tab.LastAddress = url;
            }

            if (!internalDocument)
                tab.BlockedRequests = 0;

            ResetFaviconIfHostChanged(tab, url);
            SetTabLoading(tab, !internalDocument);

            if (_activeTab == tab)
            {
                StatusText.Text = internalDocument ? "Opening Feather page…" : $"Loading {SafeHost(url)}…";
                NavigationProgress.Value = 0.08;
                NavigationProgress.Visibility = Visibility.Visible;
                UpdateFeatureButtons();
            }
        };

        page.LoadingProgressChanged += (_, progress) =>
        {
            if (!IsCurrent(tab, page) || _activeTab != tab)
                return;

            NavigationProgress.Value = Math.Max(NavigationProgress.Value, progress);
            NavigationProgress.Visibility = progress >= 1 ? Visibility.Collapsed : Visibility.Visible;
        };

        page.TitleChanged += (_, value) =>
        {
            if (!IsCurrent(tab, page) || tab.IsInternalPage)
                return;

            tab.Title.Text = string.IsNullOrWhiteSpace(value) || InternalPages.IsInternalUrl(value) ? SafeHost(tab.LastAddress) : value;
            if (_activeTab == tab)
            {
                Title = $"{tab.Title.Text} — Feather";
                TitleText.Text = tab.Title.Text;
                UpdateBookmarkButton();
            }
        };

        page.SourceChanged += (_, source) =>
        {
            if (!IsCurrent(tab, page))
                return;

            if (!tab.IsInternalPage)
            {
                tab.LastAddress =
                    source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                    source == "about:blank" ||
                    InternalPages.IsInternalUrl(source)
                        ? string.Empty
                        : source;
            }

            if (_activeTab == tab && !AddressBox.IsKeyboardFocusWithin)
            {
                AddressBox.Text = DisplayAddress(tab);
                UpdateBookmarkButton();
            }
        };

        page.HistoryChanged += (_, _) =>
        {
            if (!IsCurrent(tab, page))
                return;

            SetTabLoading(tab, page.IsLoading && !tab.IsInternalPage);
            if (_activeTab == tab)
                UpdateNavigationButtons();
        };

        page.AudioPlayingChanged += (_, playing) =>
        {
            if (!IsCurrent(tab, page) || !tab.IsLoaded)
                return;

            tab.IsPlayingAudio = playing;
            tab.UpdateStateIndicator();
            if (!playing && tab != _activeTab)
                ScheduleBackgroundLifecycle(tab);
        };

        page.DocumentCreated += (_, url) =>
        {
            if (IsCurrent(tab, page))
                InjectCosmeticFilters(tab, url);
        };

        page.NavigationCompleted += (_, e) =>
        {
            if (!IsCurrent(tab, page))
                return;

            SetTabLoading(tab, page.IsLoading && !tab.IsInternalPage);

            if (e.IsSuccess && !tab.IsInternalPage)
            {
                string source = string.IsNullOrWhiteSpace(page.Source) ? tab.LastAddress : page.Source;
                tab.LastAddress = source;
                if (!_isPrivateMode && !InternalPages.IsInternalUrl(source))
                {
                    _store.AddHistory(page.DocumentTitle, source);
                    if (_sessionTimer.IsEnabled)
                        SaveSessionSnapshot();
                }

                _ = UpdateTabFaviconAsync(tab, []);
            }

            if (_activeTab == tab)
            {
                NavigationProgress.Visibility = Visibility.Collapsed;
                StatusText.Text = e.IsSuccess
                    ? e.HttpStatusCode >= 400 ? $"Loaded with HTTP {e.HttpStatusCode}" : "Done"
                    : $"Could not load page: {e.ErrorText}";
                if (!AddressBox.IsKeyboardFocusWithin)
                    AddressBox.Text = DisplayAddress(tab);
                UpdateNavigationButtons();
                UpdateBookmarkButton();
                UpdateFeatureButtons();
                UpdateSiteIndicator();
            }
        };

        page.NewWindowRequested += async (_, e) =>
        {
            if (IsCurrent(tab, page))
                await AddTabAsync(e.Url, select: !e.InBackground, workspace: tab.Workspace, openerTab: tab);
        };

        page.DownloadStarting += (_, e) =>
        {
            if (_isClosing)
                return;

            tab.BeginDownload();
            string sourceUrl = string.IsNullOrWhiteSpace(page.Source) ? tab.LastAddress : page.Source;
            _downloadEntries[(page, e.Id)] = _isPrivateMode ? null : _store.AddDownload(e.FilePath, sourceUrl);

            string file = Path.GetFileName(e.FilePath);
            StatusText.Text = string.IsNullOrWhiteSpace(file) ? "Download started" : $"Downloading {file}";
        };

        page.DownloadUpdated += (_, e) =>
        {
            if (_isClosing || !_downloadEntries.TryGetValue((page, e.Id), out DownloadEntry? download))
                return;

            string file = Path.GetFileName(e.FilePath);
            if (e.State == DownloadState.InProgress)
            {
                if (_activeTab == tab)
                    StatusText.Text = e.TotalBytes > 0 ? $"Downloading {file} · {e.PercentComplete}%" : $"Downloading {file} · {FormatBytes(e.ReceivedBytes)}";
                return;
            }

            _downloadEntries.Remove((page, e.Id));
            tab.CompleteDownload();

            string label = e.State switch
            {
                DownloadState.Completed => "Completed",
                DownloadState.Cancelled => "Cancelled",
                _ => "Interrupted"
            };

            StatusText.Text = e.State == DownloadState.Completed ? $"Downloaded {file}" : $"Download {label.ToLowerInvariant()}: {file}";

            if (download is not null)
            {
                _store.UpdateDownload(download.Id, label, string.IsNullOrWhiteSpace(e.FilePath) ? download.FilePath : e.FilePath);
                if (_activeTab?.IsLibraryPage == true && _activeTab.LibrarySection == "downloads")
                    ShowLibraryPage(_activeTab, "downloads");
            }

            if (!tab.HasActiveDownload && tab != _activeTab)
                ScheduleBackgroundLifecycle(tab);
        };

        page.PermissionRequested += (_, request) =>
        {
            if (IsCurrent(tab, page))
                HandlePermissionRequest(tab, request);
            else
                request.Respond(null);
        };

        page.PermissionPromptDismissed += (_, promptId) => DismissPermissionPrompt(tab, promptId);

        page.ProcessFailed += async (_, reason) =>
        {
            if (IsCurrent(tab, page))
                await RecoverFailedTabAsync(tab, reason);
        };

        page.WebMessageReceived += (_, e) =>
        {
            if (!IsCurrent(tab, page) || !e.IsMainFrame)
                return;

            if (!InternalPageMessagePolicy.IsTrusted(
                    tab.IsInternalPage,
                    tab.InternalPageToken,
                    page.Source,
                    e.FrameUrl,
                    e.MessageJson))
            {
                return;
            }

            string messageJson = e.MessageJson;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => _ = HandleDeferredWebMessageAsync(tab, messageJson)));
        };

        page.FaviconUrlsChanged += async (_, urls) =>
        {
            if (IsCurrent(tab, page))
                await UpdateTabFaviconAsync(tab, urls);
        };

        page.InternalPageRequested += (_, kind) =>
        {
            if (IsCurrent(tab, page))
                Navigate(tab, InternalPages.FeatherAddressFor(kind));
        };

        page.ShortcutPressed += async (_, e) =>
        {
            if (IsCurrent(tab, page))
                await HandleShortcutAsync(e.Key, e.Modifiers);
        };

        page.StatusMessageChanged += (_, text) =>
        {
            if (!IsCurrent(tab, page) || _activeTab != tab)
                return;

            if (string.IsNullOrEmpty(text))
            {
                if (_statusBeforeHover is not null)
                    StatusText.Text = _statusBeforeHover;
                _statusBeforeHover = null;
                return;
            }

            _statusBeforeHover ??= StatusText.Text;
            StatusText.Text = text;
        };

        page.RequestsBlocked += (_, count) =>
        {
            if (!IsCurrent(tab, page))
                return;

            tab.BlockedRequests += count;
            if (_activeTab == tab)
                UpdateFeatureButtons();
        };

        page.ExternalProtocolRequested += (_, url) =>
        {
            if (IsCurrent(tab, page))
                OpenExternalProtocol(tab, url);
        };

        page.SearchRequested += async (_, text) =>
        {
            if (!IsCurrent(tab, page) || string.IsNullOrWhiteSpace(text))
                return;

            (_, string prefix) = SearchEngineInfo();
            await AddTabAsync(prefix + Uri.EscapeDataString(text), select: true, workspace: tab.Workspace, openerTab: tab);
        };

        page.FindResultChanged += (_, e) =>
        {
            if (IsCurrent(tab, page) && _activeTab == tab)
                UpdateFindResult(e);
        };

        page.CloseRequested += async (_, _) =>
        {
            if (IsCurrent(tab, page))
                await CloseTabAsync(tab);
        };
    }

    private void InjectCosmeticFilters(BrowserTab tab, string url)
    {
        if (!_shieldEnabled || !_store.Settings.CosmeticBlocking || tab.IsInternalPage ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https") ||
            _blocker.IsSiteAllowlisted(uri.Host, _store.Settings))
        {
            return;
        }

        string script = _blocker.GetCosmeticFilterScript(url);
        tab.View.ExecuteScript(
            "(() => { const run = () => {" + script + "}; " +
            "if (document.documentElement) run(); else document.addEventListener('DOMContentLoaded', run, { once: true }); })();");
    }

    private void OpenExternalProtocol(BrowserTab tab, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is "javascript" or "file" or "vbscript" or "data" or "ms-msdt" or "search-ms")
        {
            StatusText.Text = "Blocked an unsafe external link";
            return;
        }

        string site = SafeHost(tab.LastAddress);
        if (MessageBox.Show(
                $"{site} wants to open a \"{uri.Scheme}:\" link in another app.\n\n{TrimLabel(url, 120)}",
                "Open external app?",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StatusText.Text = $"No app is installed for {uri.Scheme}: links";
        }
    }

    private async Task RecoverFailedTabAsync(BrowserTab tab, string reason)
    {
        if (tab.IsClosed || _isClosing)
            return;

        if (ReferenceEquals(_videoFullscreenTab, tab))
        {
            _videoFullscreenTab = null;
            ApplyFullscreen();
        }

        bool wasActive = _activeTab == tab;
        if (!tab.IsInternalPage && tab.IsLoaded)
        {
            string source = tab.View.Source;
            if (!string.IsNullOrWhiteSpace(source) &&
                !source.Equals("about:blank", StringComparison.OrdinalIgnoreCase) &&
                !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                !InternalPages.IsInternalUrl(source))
            {
                tab.LastAddress = source;
            }
        }

        CancelSleepSchedule(tab);
        DismissPermissionPrompt(tab, promptId: null);
        SetTabLoading(tab, false);
        ForgetPageDownloads(tab, tab.View);
        RemovePageControl(tab.View);
        tab.View.Dispose();
        tab.View = CreatePageFor(tab);
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
        StatusText.Text = $"Tab renderer stopped ({reason}) · recovering…";
        try
        {
            await EnsureTabLoadedAsync(tab);
            if (tab.IsClosed || _isClosing)
                return;

            if (_activeTab == tab)
            {
                tab.View.SetVisible(
                    CommandPaletteOverlay.Visibility != Visibility.Visible && WindowState != WindowState.Minimized,
                    zIndex: 10);
                AddressBox.Text = DisplayAddress(tab);
            }

            StatusText.Text = "Tab recovered";
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException or TimeoutException)
        {
            Trace.TraceWarning($"Tab recovery failed: {ex.Message}");
            StatusText.Text = "Tab recovery failed · press Ctrl+R to retry";
        }

        UpdateResourceText();
    }
}
