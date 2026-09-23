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
using FeatherBrowser.Features.Blocking;
using FeatherBrowser.Features.Passwords;
using FeatherBrowser.Infrastructure.Persistence;
using FeatherBrowser.Presentation.Commands;
using FeatherBrowser.Presentation.Tabs;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow : Window
{
    private readonly List<BrowserTab> _tabs = [];
    private readonly Stack<string> _closedTabs = new();
    private readonly BrowserDataStore _store = new();
    private readonly BlockerEngine _blocker = new();
    private readonly PasswordVault _passwordVault = new();
    private readonly Brush _activeTabBrush;
    private readonly Brush _inactiveTabBrush;
    private readonly Brush _accentBrush;
    private readonly Brush _gameAccentBrush;
    private readonly Brush _secondaryBrush;
    private readonly DispatcherTimer _resourceTimer;
    private readonly DispatcherTimer _sessionTimer;
    private readonly List<CommandPaletteEntry> _paletteItems = [];
    private readonly string? _initialAddress;
    private readonly bool _isPrivateMode;
    private readonly string? _privateDataRoot;

    private CoreWebView2Environment? _environment;
    private BrowserTab? _activeTab;
    private bool _ecoMode;
    private bool _shieldEnabled;
    private bool _isClosing;
    private bool _isFullscreen;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    private DateTime _lastMemoryGuardUtc = DateTime.MinValue;
    private long _lastObservedMemoryBytes;

    public bool IsPrivateMode => _isPrivateMode;

    public MainWindow(string? initialAddress = null, bool privateMode = false)
    {
        InitializeComponent();
        _initialAddress = initialAddress;
        _isPrivateMode = privateMode;
        CleanupStalePrivateProfiles();
        _privateDataRoot = privateMode
            ? Path.Combine(Path.GetTempPath(), "FeatherBrowser", "Private", Guid.NewGuid().ToString("N"))
            : null;
        _activeTabBrush = (Brush)FindResource("TabActiveBackground");
        _inactiveTabBrush = (Brush)FindResource("TabBackground");
        _accentBrush = (Brush)FindResource("Accent");
        _gameAccentBrush = (Brush)FindResource("GameAccent");
        _secondaryBrush = (Brush)FindResource("TextSecondary");
        _ecoMode = _store.Settings.EcoMode;
        _shieldEnabled = _store.Settings.ShieldEnabled;
        _blocker.Reload(_store.Settings.CustomBlockRules);
        NormalizeWorkspaceSettings();
        ApplyUiPreferences();
        UpdateWorkspaceSidebar();
        ApplyPrivateWindowChrome();

        _resourceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _resourceTimer.Tick += (_, _) => UpdateResourceText();
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _sessionTimer.Tick += (_, _) =>
        {
            if (!_isPrivateMode && _store.Settings.CrashRecoveryAutosave && !_isClosing)
                SaveSessionSnapshot();
        };

        Loaded += async (_, _) => await InitializeBrowserAsync();
        UpdateFeatureButtons();
    }
}
