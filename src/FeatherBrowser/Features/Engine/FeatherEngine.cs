using System.Diagnostics;
using System.Globalization;
using System.IO;
using CefSharp;
using CefSharp.Wpf.HwndHost;

namespace FeatherBrowser.Features.Engine;
internal static class FeatherEngine
{
    public const string InternalHost = "feather.internal";
    public const string InternalOrigin = "https://" + InternalHost;

    private static readonly object Gate = new();
    private static readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static bool _initialized;
    public static Task WhenReady => Ready.Task;

    public static bool IsInitialized => _initialized;

    public static string DataRoot { get; private set; } = string.Empty;
    public static string ChromiumVersion =>
        _initialized ? $"Chromium {Cef.ChromiumVersion} · CEF {Cef.CefVersion}" : "Chromium (not started)";

    public static int RunSubprocessIfRequested(string[] args) =>
        CefSharp.BrowserSubprocess.SelfHost.Main(args);

    public static void Initialize(EngineStartupOptions options)
    {
        lock (Gate)
        {
            if (_initialized)
                return;

            DataRoot = options.DataRoot;
            Directory.CreateDirectory(DataRoot);

            var settings = new CefSettings
            {
                RootCachePath = DataRoot,
                CachePath = Path.Combine(DataRoot, "Default"),
                BrowserSubprocessPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName,
                Locale = CultureInfo.CurrentUICulture.Name is { Length: > 0 } locale ? locale : "en-US",
                AcceptLanguageList = BuildAcceptLanguageList(),
                PersistSessionCookies = options.PersistSessionCookies,
                LogSeverity = LogSeverity.Warning,
                LogFile = Path.Combine(DataRoot, "engine.log"),
                BackgroundColor = Cef.ColorSetARGB(255, options.BackgroundColor.R, options.BackgroundColor.G, options.BackgroundColor.B),
            };

            settings.CefCommandLineArgs["enable-features"] =
                "IntensiveWakeUpThrottling,CalculateNativeWinOcclusion,BackForwardCache";

            if (options.ForcedDarkMode is bool dark)
                settings.CefCommandLineArgs["blink-settings"] = dark ? "preferredColorScheme=0" : "preferredColorScheme=1";

            if (options.RendererProcessLimit is int limit and > 0)
                settings.CefCommandLineArgs["renderer-process-limit"] = limit.ToString(CultureInfo.InvariantCulture);
            CefSharpSettings.ShutdownOnExit = false;

            try
            {
                if (!Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null))
                    throw new InvalidOperationException("Chromium failed to initialize. See engine.log in the Feather data folder.");

                InternalPages.Register(Cef.GetGlobalRequestContext());
                _initialized = true;
                Ready.TrySetResult();
            }
            catch (Exception ex)
            {
                Ready.TrySetException(ex);
                throw;
            }
        }
    }
    public static IRequestContext CreatePrivateRequestContext()
    {
        EnsureInitialized();
        var context = new RequestContext(new RequestContextSettings
        {
            CachePath = string.Empty,
            PersistSessionCookies = false,
        });
        InternalPages.Register(context);
        return context;
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            if (!_initialized)
                return;

            _initialized = false;
            try
            {
                Cef.Shutdown();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Chromium shutdown failed: {ex.Message}");
            }
        }
    }

    internal static void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("Feather's engine has not been initialized.");
    }

    private static string BuildAcceptLanguageList()
    {
        string name = CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrWhiteSpace(name))
            return "en-US,en";

        string neutral = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return string.Equals(name, neutral, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name},{neutral}";
    }
}

internal sealed record EngineStartupOptions
{
    public required string DataRoot { get; init; }
    public bool PersistSessionCookies { get; init; } = true;
    public int? RendererProcessLimit { get; init; }
    public System.Windows.Media.Color BackgroundColor { get; init; } = System.Windows.Media.Color.FromRgb(24, 24, 24);
    public bool? ForcedDarkMode { get; init; }
}
