using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using FeatherBrowser.Features.Blocking;
using FeatherBrowser.Features.Passwords;
using FeatherBrowser.Infrastructure.Persistence;
using FeatherBrowser.Presentation.Commands;
using FeatherBrowser.Presentation.Tabs;
using Microsoft.Web.WebView2.Core;
using FeatherBrowser.Features.Sync;
using FeatherBrowser.Features.Sync.Devices;
using FeatherBrowser.Infrastructure.Supabase;
using FeatherBrowser.Presentation.Account;

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

    private readonly SupabaseConfig _supabaseConfig;
    private readonly SupabaseAuthClient _supabaseAuth;
    private readonly SupabaseMfaClient _supabaseMfa;
    private readonly SupabaseSyncClient _supabaseSync;
    private readonly SupabaseDeviceClient _supabaseDevices;

    private readonly ZeroKnowledgeSyncService _zeroKnowledgeSync;
    private readonly DeviceApprovalService _deviceApproval;
    private readonly SyncDeviceSecretStore _syncDeviceSecretStore;
    private readonly SupabaseSessionStore _supabaseSessionStore;
    private readonly DeviceIdentityStore _deviceIdentityStore;
    private readonly SyncSnapshotService _syncSnapshotService;
    private readonly SyncLocalStateStore _syncLocalStateStore;
    private readonly SyncCoordinator _syncCoordinator;
    private readonly DispatcherTimer _syncTimer;

    private SupabaseSession? _accountSession;
    private TotpEnrollment? _pendingTotpEnrollment;
    private AccountPageState _accountPageState = new();
    private bool _syncBusy;

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

        _supabaseConfig = FeatherSupabase.CreateConfig();

        _supabaseAuth = new SupabaseAuthClient(_supabaseConfig);
        _supabaseMfa = new SupabaseMfaClient(_supabaseConfig);
        _supabaseSync = new SupabaseSyncClient(_supabaseConfig);
        _supabaseDevices = new SupabaseDeviceClient(_supabaseConfig);

        _syncDeviceSecretStore = new SyncDeviceSecretStore();
        _supabaseSessionStore = new SupabaseSessionStore();
        _deviceIdentityStore = new DeviceIdentityStore();
        _syncSnapshotService = new SyncSnapshotService();
        _syncLocalStateStore = new SyncLocalStateStore();

        _zeroKnowledgeSync = new ZeroKnowledgeSyncService(
            _supabaseSync,
            _syncDeviceSecretStore
        );

        _deviceApproval = new DeviceApprovalService(
            _supabaseDevices,
            _supabaseSync,
            _deviceIdentityStore,
            _syncDeviceSecretStore
        );

        _syncCoordinator = new SyncCoordinator(
            _store,
            _zeroKnowledgeSync,
            _syncSnapshotService,
            _syncLocalStateStore
        );

        _accountPageState = AccountPageState.SignedOut(_isPrivateMode);

        CleanupStalePrivateProfiles();

        _privateDataRoot = privateMode
            ? Path.Combine(
                Path.GetTempPath(),
                "FeatherBrowser",
                "Private",
                Guid.NewGuid().ToString("N"))
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

        _resourceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        _resourceTimer.Tick += (_, _) =>
            UpdateResourceText();

        _sessionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };

        _sessionTimer.Tick += (_, _) =>
        {
            if (!_isPrivateMode &&
                _store.Settings.CrashRecoveryAutosave &&
                !_isClosing)
            {
                SaveSessionSnapshot();
            }
        };

        _syncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(Math.Clamp(_store.Settings.AutoSyncMinutes, 1, 60))
        };

        _syncTimer.Tick += async (_, _) =>
            await AutomaticSyncTickAsync();

        Loaded += async (_, _) =>
        {
            await InitializeBrowserAsync();
            await InitializeAccountAsync();
        };

        UpdateFeatureButtons();
    }

    protected override void OnClosed(EventArgs e)
    {
        _syncTimer.Stop();
        _syncCoordinator.Dispose();
        _supabaseDevices.Dispose();
        _supabaseSync.Dispose();
        _supabaseMfa.Dispose();
        _supabaseAuth.Dispose();

        base.OnClosed(e);
    }
}
