using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CefSharp;
using CefSharp.DevTools;
using CefSharp.DevTools.Memory;
using CefSharp.DevTools.Page;
using HwndBrowser = CefSharp.Wpf.HwndHost.ChromiumWebBrowser;

namespace FeatherBrowser.Features.Engine;

internal sealed class WebPageOptions
{
    public required INetworkPolicy Network { get; init; }

    public IRequestContext? RequestContext { get; init; }

    public Func<System.Windows.Input.Key, System.Windows.Input.ModifierKeys, bool> IsShortcut { get; init; } = static (_, _) => false;

    public Func<System.Windows.Input.Key, System.Windows.Input.ModifierKeys, bool> IsReservedShortcut { get; init; } = static (_, _) => false;

    public required Func<string> DownloadFolder { get; init; }

    public Task NavigationGate { get; init; } = Task.CompletedTask;
}

internal sealed partial class WebPage : IDisposable
{
    private readonly record struct DownloadProgress(int Bucket, long ReportedAt);

    private static readonly TimeSpan CreationTimeout = TimeSpan.FromSeconds(30);

    private HwndBrowser? _browser;
    private readonly Dispatcher _dispatcher;
    private readonly WebPageOptions _options;
    private readonly TaskCompletionSource _created = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, bool> _audibleFrames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _editedFrames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _pipFrames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, DownloadProgress> _downloads = new();
    private readonly ConcurrentDictionary<PermissionRequest, byte> _pendingPermissions = new();
    private readonly ResourceRequestHandlerImpl _resourceHandler;

    private DevToolsClient? _devTools;
    private volatile string _source = string.Empty;
    private volatile string _title = string.Empty;
    private volatile string? _internalNonce;
    private volatile bool _disposed;
    private volatile bool _isLoading;
    private volatile bool _canGoBack;
    private volatile bool _canGoForward;
    private string? _pendingNavigation;
    private string? _loadError;
    private int _pendingBlocked;
    private int _browserId = -1;
    private bool _muted;
    private bool _visible;
    private bool _suspended;
    private Task _resumeTask = Task.CompletedTask;
    private bool _audible;
    private int _findIdentifier;

    public WebPage(WebPageOptions options)
    {
        FeatherEngine.EnsureInitialized();

        _options = options;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _resourceHandler = new ResourceRequestHandlerImpl(this);
    }

    private HwndBrowser CreateBrowser()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var browser = new HwndBrowser
        {
            Visibility = _visible ? Visibility.Visible : Visibility.Collapsed,
            Focusable = true,
        };

        if (_options.RequestContext is not null)
            browser.RequestContext = _options.RequestContext;

        browser.JavascriptObjectRepository.Settings.JavascriptBindingApiAllowOrigins = [FeatherEngine.InternalOrigin];

        browser.RequestHandler = new RequestHandlerImpl(this);
        browser.LifeSpanHandler = new LifeSpanHandlerImpl(this);
        browser.DisplayHandler = new DisplayHandlerImpl(this);
        browser.KeyboardHandler = new KeyboardHandlerImpl(this);
        browser.MenuHandler = new ContextMenuHandlerImpl(this);
        browser.DownloadHandler = new DownloadHandlerImpl(this);
        browser.PermissionHandler = new PermissionHandlerImpl(this);
        browser.FindHandler = new FindHandlerImpl(this);
        browser.RenderProcessMessageHandler = new RenderProcessHandlerImpl(this);
        browser.FocusHandler = new FocusHandlerImpl(this);

        browser.LoadingStateChanged += OnLoadingStateChanged;
        browser.FrameLoadStart += OnFrameLoadStart;
        browser.FrameLoadEnd += OnFrameLoadEnd;
        browser.LoadError += OnLoadError;
        browser.JavascriptMessageReceived += OnJavascriptMessageReceived;
        return browser;
    }

    private HwndBrowser Browser => _browser ?? throw new InvalidOperationException("The page has not been created.");


    public event EventHandler<string>? TitleChanged;
    public event EventHandler<string>? SourceChanged;
    public event EventHandler<string>? NavigationStarting;
    public event EventHandler<NavigationCompletedArgs>? NavigationCompleted;
    public event EventHandler? HistoryChanged;
    public event EventHandler<double>? LoadingProgressChanged;
    public event EventHandler<NewWindowRequestedArgs>? NewWindowRequested;
    public event EventHandler<DownloadEventArgs>? DownloadStarting;
    public event EventHandler<DownloadEventArgs>? DownloadUpdated;
    public event EventHandler<PermissionRequest>? PermissionRequested;
    public event EventHandler<ulong>? PermissionPromptDismissed;
    public event EventHandler<WebMessageArgs>? WebMessageReceived;
    public event EventHandler<bool>? AudioPlayingChanged;
    public event EventHandler<bool>? FullscreenChanged;
    public event EventHandler<string>? ProcessFailed;
    public event EventHandler<LoginSubmission>? LoginSubmitted;
    public event EventHandler? FocusReceived;

    private sealed class FocusHandlerImpl(WebPage page) : CefSharp.Handler.FocusHandler
    {
        protected override void OnGotFocus(IWebBrowser chromiumWebBrowser, IBrowser browser)
        {
            base.OnGotFocus(chromiumWebBrowser, browser);
            if (!browser.IsPopup)
                page.Post(() => page.FocusReceived?.Invoke(page, EventArgs.Empty));
        }
    }

    internal void OnLoginSubmitted(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string origin = root.GetProperty("o").GetString() ?? "";
            string username = root.GetProperty("u").GetString() ?? "";
            string password = root.GetProperty("p").GetString() ?? "";
            if (password.Length is 0 or > 1024 || username.Length > 512)
                return;

            Post(() =>
            {
                if (!Uri.TryCreate(_source, UriKind.Absolute, out Uri? current) ||
                    !string.Equals(current.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                LoginSubmitted?.Invoke(this, new LoginSubmission(origin, username, password));
            });
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Trace.TraceWarning("Ignored a malformed login report.");
        }
    }
    public event EventHandler<IReadOnlyList<string>>? FaviconUrlsChanged;
    public event EventHandler<string>? InternalPageRequested;
    public event EventHandler<ShortcutArgs>? ShortcutPressed;
    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler<int>? RequestsBlocked;
    public event EventHandler<string>? DocumentCreated;
    public event EventHandler<string>? ExternalProtocolRequested;
    public event EventHandler<string>? SearchRequested;
    public event EventHandler<FindResultArgs>? FindResultChanged;
    public event EventHandler? CloseRequested;


    public FrameworkElement Control => _browser ??= CreateBrowser();

    public void SetVisible(bool visible, int zIndex)
    {
        _visible = visible;
        if (_browser is null)
            return;

        System.Windows.Controls.Panel.SetZIndex(_browser, zIndex);

        if (!visible)
        {
            _browser.Visibility = Visibility.Collapsed;
            return;
        }
        if (_resumeTask.IsCompleted)
            _browser.Visibility = Visibility.Visible;
        else
            _ = ShowAfterResumeAsync();
    }

    private async Task ShowAfterResumeAsync()
    {
        try
        {
            await _resumeTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            Trace.TraceWarning("Page resume is slow; showing the page anyway.");
        }

        if (_visible && !_disposed && _browser is not null)
            _browser.Visibility = Visibility.Visible;
    }

    public bool IsVisible => _visible;

    public bool HasControl => _browser is not null;

    public bool IsCreated => _created.Task.IsCompletedSuccessfully;

    public bool IsDisposed => _disposed;

    public string Source => _source;

    public string DocumentTitle => _title;

    public bool CanGoBack => IsCreated && _canGoBack;

    public bool CanGoForward => IsCreated && _canGoForward;

    public bool IsLoading => IsCreated && _isLoading;

    public bool IsSuspended => _suspended;

    public bool IsPlayingAudio => _audible;
    public bool HasUnsavedEdits => !_editedFrames.IsEmpty;
    public bool IsInPictureInPicture => !_pipFrames.IsEmpty;

    public bool IsShowingInternalPage => InternalPages.IsInternalUrl(_source);

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (TryGetHost() is { } host)
                host.SetAudioMuted(value);
        }
    }

    public double ZoomFactor
    {
        get => IsCreated ? Math.Pow(1.2, Browser.ZoomLevel) : 1.0;
        set
        {
            if (!IsCreated)
                return;

            double factor = Math.Clamp(value, 0.25, 5.0);
            Browser.ZoomLevel = Math.Log(factor) / Math.Log(1.2);
        }
    }

    public async Task EnsureCreatedAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Task finished = await Task.WhenAny(_created.Task, Task.Delay(CreationTimeout));
        if (finished != _created.Task)
            throw new TimeoutException("Chromium did not create the page in time.");
        await _created.Task;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        InternalPages.Revoke(_internalNonce);

        foreach (PermissionRequest request in _pendingPermissions.Keys)
            request.Respond(null);
        _pendingPermissions.Clear();

        try { _devTools?.Dispose(); }
        catch (Exception ex) when (FeatherBrowser.Infrastructure.ExceptionFilters.IsRecoverable(ex)) { Trace.TraceWarning($"DevTools client disposal failed: {ex.Message}"); }
        _devTools = null;

        if (_browser is { } browser)
        {
            browser.LoadingStateChanged -= OnLoadingStateChanged;
            browser.FrameLoadStart -= OnFrameLoadStart;
            browser.FrameLoadEnd -= OnFrameLoadEnd;
            browser.LoadError -= OnLoadError;
            browser.JavascriptMessageReceived -= OnJavascriptMessageReceived;

            try { browser.Dispose(); }
            catch (Exception ex) when (FeatherBrowser.Infrastructure.ExceptionFilters.IsRecoverable(ex)) { Trace.TraceWarning($"Browser disposal failed: {ex.Message}"); }
        }

        _created.TrySetCanceled();
    }


    public void Navigate(string address)
    {
        if (_disposed)
            return;

        if (!IsCreated || !_options.NavigationGate.IsCompleted)
        {
            _pendingNavigation = address;
            if (IsCreated)
                FlushPendingNavigationWhenReady();
            return;
        }

        _pendingNavigation = null;
        Browser.LoadUrl(address);
    }

    private void FlushPendingNavigationWhenReady()
    {
        if (_options.NavigationGate.IsCompleted)
        {
            if (_pendingNavigation is { } pending && !_disposed && IsCreated)
            {
                _pendingNavigation = null;
                Browser.LoadUrl(pending);
            }
            return;
        }

        _options.NavigationGate.ContinueWith(
            _ => Post(FlushPendingNavigationWhenReady),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void NavigateToInternalPage(string kind, string html)
    {
        if (_disposed || !IsCreated)
            return;

        _pendingNavigation = null;
        string url = InternalPages.Publish(_browserId, kind, html, _internalNonce);
        _internalNonce = url[(url.LastIndexOf('/') + 1)..];
        Browser.LoadUrl(url);
    }

    public void GoBack()
    {
        if (CanGoBack)
            Browser.Back();
    }

    public void GoForward()
    {
        if (CanGoForward)
            Browser.Forward();
    }

    public void Reload(bool ignoreCache = false)
    {
        if (IsCreated)
            Browser.Reload(ignoreCache);
    }

    public void Stop()
    {
        if (IsCreated)
            Browser.Stop();
    }

    public async Task<string> ExecuteScriptAsync(string script)
    {
        if (_disposed || !IsCreated || !Browser.CanExecuteJavascriptInMainFrame)
            return "null";

        JavascriptResponse response = await Browser.EvaluateScriptAsync(script, TimeSpan.FromSeconds(10));
        return response.Success ? JsonSerializer.Serialize(response.Result) : "null";
    }

    public void ExecuteScript(string script)
    {
        if (!_disposed && IsCreated && Browser.CanExecuteJavascriptInMainFrame)
            Browser.ExecuteScriptAsync(script);
    }

    public void PostWebMessageAsJson(string json)
    {
        if (IsShowingInternalPage)
            ExecuteScript($"window.__featherReceive && window.__featherReceive({json});");
    }

    public async Task<bool> TrySuspendAsync()
    {
        if (_disposed || !IsCreated || _suspended)
            return _suspended;

        try
        {
            DevToolsMethodResponse response = await DevTools().Page.SetWebLifecycleStateAsync(SetWebLifecycleStateState.Frozen);
            _suspended = response.Success;
        }
        catch (Exception ex) when (ex is DevToolsClientException or ObjectDisposedException or InvalidOperationException)
        {
            Trace.TraceWarning($"Page freeze failed: {ex.Message}");
            _suspended = false;
        }

        return _suspended;
    }

    public void Resume()
    {
        if (!_suspended || _disposed || !IsCreated)
            return;

        _suspended = false;
        _resumeTask = ResumeCoreAsync();
    }

    private async Task ResumeCoreAsync()
    {
        try
        {
            await DevTools().Page.SetWebLifecycleStateAsync(SetWebLifecycleStateState.Active);
        }
        catch (Exception ex) when (ex is DevToolsClientException or ObjectDisposedException or InvalidOperationException)
        {
            Trace.TraceWarning($"Page resume failed: {ex.Message}");
        }
        if (_visible && !_disposed && _browser is { Visibility: Visibility.Visible } browser)
        {
            browser.Visibility = Visibility.Collapsed;
            browser.Visibility = Visibility.Visible;
        }
    }

    public async Task<long?> GetPageMemoryAsync()
    {
        if (_disposed || !IsCreated)
            return null;

        try
        {
            var usage = await DevTools().Runtime.GetHeapUsageAsync().WaitAsync(TimeSpan.FromSeconds(2));
            return (long)(usage.UsedSize + usage.EmbedderHeapUsedSize + usage.BackingStorageSize);
        }
        catch (Exception ex) when (ex is DevToolsClientException or ObjectDisposedException or InvalidOperationException or TimeoutException)
        {
            Trace.TraceWarning($"Page memory sample failed: {ex.Message}");
            return null;
        }
    }

    public async Task TrimMemoryAsync()
    {
        if (_disposed || !IsCreated)
            return;

        try
        {
            await DevTools().Memory.SimulatePressureNotificationAsync(PressureLevel.Critical);
        }
        catch (Exception ex) when (ex is DevToolsClientException or ObjectDisposedException or InvalidOperationException)
        {
            Trace.TraceWarning($"Memory pressure signal failed: {ex.Message}");
        }
    }

    public async Task<int> ClearCookiesAsync(string url)
    {
        if (_disposed || !IsCreated)
            return 0;

        IRequestContext context = Browser.RequestContext ?? Cef.GetGlobalRequestContext();
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? site) || string.IsNullOrEmpty(site.Host))
            return 0;

        ICookieManager manager = await context.GetCookieManagerAsync();
        List<Cookie> cookies = (await manager.VisitAllCookiesAsync())
            .Where(cookie => CookieAppliesToHost(cookie.Domain, site.Host))
            .ToList();

        foreach (Cookie cookie in cookies)
        {
            string domain = cookie.Domain.TrimStart('.');
            string scheme = cookie.Secure ? Uri.UriSchemeHttps : site.Scheme;
            await manager.DeleteCookiesAsync($"{scheme}://{domain}{cookie.Path}", cookie.Name);
        }

        return cookies.Count;
    }

    internal static bool CookieAppliesToHost(string? cookieDomain, string host)
    {
        if (string.IsNullOrWhiteSpace(cookieDomain))
            return false;

        string domain = cookieDomain.TrimStart('.');
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
               (cookieDomain.StartsWith('.') && host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }


    public async Task<byte[]?> FetchImageAsync(string url, CancellationToken token)
    {
        if (_disposed || !IsCreated ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https") ||
            _options.Network.ShouldBlock(uri, _source, WebResourceKind.Image))
        {
            return null;
        }

        IRequestContext context = Browser.RequestContext ?? Cef.GetGlobalRequestContext();
        var result = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var buffer = new MemoryStream();
        object gate = new();
        bool finished = false;
        bool tooLarge = false;
        IUrlRequest? pending = null;

        IUrlRequestClient client = CefSharp.Fluent.UrlRequestClient.Create()
            .OnDownloadData((_, data) =>
            {
                lock (gate)
                {
                    if (finished || tooLarge)
                        return;
                    data.CopyTo(buffer);
                    if (buffer.Length > FaviconLoader.MaxBytes)
                    {
                        tooLarge = true;
                        buffer.SetLength(0);
                    }
                }
            })
            .OnRequestComplete(request =>
            {
                lock (gate)
                {
                    if (finished)
                        return;
                    IResponse? response = request.Response;
                    bool ok = !tooLarge &&
                              request.RequestStatus == UrlRequestStatus.Success &&
                              response is { StatusCode: >= 200 and < 300 } &&
                              FaviconLoader.IsImageType(response.MimeType) &&
                              buffer.Length > 0;
                    result.TrySetResult(ok ? buffer.ToArray() : null);
                }
            })
            .Build();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using (timeout.Token.Register(() => result.TrySetResult(null)))
            {
                pending = await Cef.UIThreadTaskFactory.StartNew(() => StartFaviconRequest(uri, client, context));

                return await result.Task;
            }
        }
        finally
        {
            lock (gate)
                finished = true;

            if (pending is { } urlRequest)
            {
                _ = Cef.UIThreadTaskFactory.StartNew(urlRequest.Dispose);
            }
        }
    }
    private static IUrlRequest StartFaviconRequest(Uri uri, IUrlRequestClient client, IRequestContext context)
    {
        using IRequest request = new Request();
        request.Url = uri.AbsoluteUri;
        request.Method = "GET";
        return new UrlRequest(request, client, context);
    }

    public void ShowDevTools() => TryGetHost()?.ShowDevTools();
    public async Task<string> TogglePictureInPictureAsync()
    {
        if (_disposed || !IsCreated)
            return "failed";

        const string script = """
            (async () => {
              if (document.pictureInPictureElement) { await document.exitPictureInPicture(); return "exited"; }
              const videos = [...document.querySelectorAll("video")]
                .filter(v => v.readyState > 0 && !v.disablePictureInPicture);
              if (!videos.length) return "no-video";
              const area = v => { const r = v.getBoundingClientRect(); return r.width * r.height; };
              const playing = videos.filter(v => !v.paused && !v.ended);
              const video = (playing.length ? playing : videos).sort((a, b) => area(b) - area(a))[0];
              await video.requestPictureInPicture();
              return "entered";
            })()
            """;

        try
        {
            DevToolsMethodResponse response = await DevTools().ExecuteDevToolsMethodAsync(
                "Runtime.evaluate",
                new Dictionary<string, object>
                {
                    ["expression"] = script,
                    ["awaitPromise"] = true,
                    ["userGesture"] = true,
                    ["returnByValue"] = true
                }).WaitAsync(TimeSpan.FromSeconds(5));

            if (!response.Success)
                return "failed";

            using JsonDocument result = JsonDocument.Parse(response.ResponseAsJsonString);
            JsonElement root = result.RootElement;
            if (root.TryGetProperty("exceptionDetails", out _))
                return "failed";

            return root.TryGetProperty("result", out JsonElement value) &&
                   value.TryGetProperty("value", out JsonElement text) &&
                   text.ValueKind == JsonValueKind.String
                ? text.GetString() ?? "failed"
                : "failed";
        }
        catch (Exception ex) when (ex is DevToolsClientException or ObjectDisposedException or InvalidOperationException or TimeoutException or JsonException)
        {
            Trace.TraceWarning($"Picture-in-picture failed: {ex.Message}");
            return "failed";
        }
    }

    public void Print() => TryGetHost()?.Print();

    public void Find(string text, bool forward = true, bool findNext = false, bool matchCase = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            StopFinding(clearSelection: true);
            return;
        }

        if (!findNext)
            _findIdentifier++;
        TryGetHost()?.Find(text, forward, matchCase, findNext);
    }

    public void StopFinding(bool clearSelection) => TryGetHost()?.StopFinding(clearSelection);

    public void Focus()
    {
        if (IsCreated)
            TryGetHost()?.SetFocus(true);
    }

    private IBrowserHost? TryGetHost()
    {
        if (_disposed || !IsCreated)
            return null;

        try
        {
            IBrowser? browser = Browser.GetBrowser();
            return browser is { IsDisposed: false } ? browser.GetHost() : null;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            return null;
        }
    }

    private DevToolsClient DevTools()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _devTools ??= Browser.GetDevToolsClient();
    }
    
    private void Post(Action action)
    {
        if (_disposed)
            return;

        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (!_disposed)
                action();
        });
    }

    private void OnBrowserCreated(IBrowser browser)
    {
        Volatile.Write(ref _browserId, browser.Identifier);
        Post(() =>
        {
            if (_muted && TryGetHostUnchecked(browser) is { } host)
                host.SetAudioMuted(true);

            _created.TrySetResult();
            FlushPendingNavigationWhenReady();
        });
    }

    private static IBrowserHost? TryGetHostUnchecked(IBrowser browser)
    {
        try { return browser.IsDisposed ? null : browser.GetHost(); }
        catch (ObjectDisposedException) { return null; }
    }

    private void OnRequestBlocked()
    {
        if (Interlocked.Increment(ref _pendingBlocked) != 1)
            return;

        Post(() =>
        {
            int count = Interlocked.Exchange(ref _pendingBlocked, 0);
            if (count > 0)
                RequestsBlocked?.Invoke(this, count);
        });
    }

    private void UpdateAudibleState()
    {
        bool audible = _audibleFrames.Values.Any(static playing => playing);
        Post(() =>
        {
            if (_audible == audible)
                return;
            _audible = audible;
            AudioPlayingChanged?.Invoke(this, audible);
        });
    }

    private void OnLoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        if (e.Browser.IsPopup)
            return;

        _isLoading = e.IsLoading;
        _canGoBack = e.CanGoBack;
        _canGoForward = e.CanGoForward;
        Post(() => HistoryChanged?.Invoke(this, EventArgs.Empty));
    }

    private void OnFrameLoadStart(object? sender, FrameLoadStartEventArgs e)
    {
        if (e.Browser.IsPopup || !e.Frame.IsMain)
            return;

        Volatile.Write(ref _loadError, null);
        _audibleFrames.Clear();
        _editedFrames.Clear();
        _pipFrames.Clear();
        UpdateAudibleState();
    }

    private void OnLoadError(object? sender, LoadErrorEventArgs e)
    {
        if (e.Browser.IsPopup || !e.Frame.IsMain || e.ErrorCode == CefErrorCode.Aborted)
            return;

        Volatile.Write(ref _loadError, string.IsNullOrWhiteSpace(e.ErrorText) ? e.ErrorCode.ToString() : e.ErrorText);
    }

    private void OnFrameLoadEnd(object? sender, FrameLoadEndEventArgs e)
    {
        if (e.Browser.IsPopup || !e.Frame.IsMain)
            return;

        string? error = Volatile.Read(ref _loadError);
        int status = e.HttpStatusCode;
        Post(() => NavigationCompleted?.Invoke(this, new NavigationCompletedArgs(error is null, error, status)));
    }

    private void OnJavascriptMessageReceived(object? sender, JavascriptMessageReceivedEventArgs e)
    {
        if (e.Browser.IsPopup || e.Frame is null)
            return;

        string json = e.Message as string ?? JsonSerializer.Serialize(e.Message);
        string frameUrl = e.Frame.Url ?? string.Empty;
        bool isMain = e.Frame.IsMain;
        Post(() => WebMessageReceived?.Invoke(this, new WebMessageArgs(json, frameUrl, isMain)));
    }
}
