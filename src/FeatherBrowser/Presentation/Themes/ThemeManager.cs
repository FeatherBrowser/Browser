using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace FeatherBrowser.Presentation.Themes;

internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Dictionary<string, (string Dark, string Light)> Palette = new()
    {
        ["ChromeBrush"] = ("#1F1F1F", "#F3F3F3"),
        ["ContentBrush"] = ("#181818", "#FFFFFF"),
        ["TabActiveBrush"] = ("#333333", "#FFFFFF"),
        ["TabHoverBrush"] = ("#2A2A2A", "#E7E7E7"),
        ["FieldBrush"] = ("#2B2B2B", "#FFFFFF"),
        ["FieldHoverBrush"] = ("#323232", "#FBFBFB"),
        ["FieldBorderBrush"] = ("#2B2B2B", "#D9D9D9"),
        ["DividerBrush"] = ("#2D2D2D", "#E0E0E0"),
        ["BorderBrush"] = ("#3B3B3B", "#D1D1D1"),
        ["HoverBrush"] = ("#17FFFFFF", "#0F000000"),
        ["PressedBrush"] = ("#24FFFFFF", "#1C000000"),
        ["TextPrimaryBrush"] = ("#E6E6E6", "#1B1B1B"),
        ["TextSecondaryBrush"] = ("#A3A3A3", "#5C5C5C"),
        ["TextMutedBrush"] = ("#757575", "#8A8A8A"),
        ["AccentBrush"] = ("#5AA2F0", "#0F6CBD"),
        ["AccentTextBrush"] = ("#0E1520", "#FFFFFF"),
        ["AccentSubtleBrush"] = ("#24384F", "#DDEBF9"),
        ["SuccessBrush"] = ("#5DC28C", "#107C41"),
        ["WarningBrush"] = ("#E2A33C", "#9D5D00"),
        ["DangerBrush"] = ("#EF6B6B", "#C42B1C"),
        ["MenuBrush"] = ("#272727", "#FFFFFF"),
        ["MenuBorderBrush"] = ("#3A3A3A", "#D6D6D6"),
        ["MenuHoverBrush"] = ("#343434", "#F0F0F0"),
        ["ScrimBrush"] = ("#99000000", "#59000000"),
        ["CaptionCloseHoverBrush"] = ("#C42B1C", "#C42B1C"),
        ["CaptionCloseTextBrush"] = ("#FFFFFF", "#FFFFFF"),
        ["PrivateChromeBrush"] = ("#2A2433", "#ECE7F4"),
        ["PrivateTabActiveBrush"] = ("#3B3248", "#FBF9FE"),
    };

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["ChromeBackground"] = "ChromeBrush",
        ["ToolbarBackground"] = "ChromeBrush",
        ["TabBackground"] = "ChromeBrush",
        ["TabActiveBackground"] = "TabActiveBrush",
        ["ControlBackground"] = "FieldBrush",
        ["ControlHover"] = "HoverBrush",
        ["ControlPressed"] = "PressedBrush",
        ["PanelBackground"] = "MenuBrush",
        ["PanelBorder"] = "BorderBrush",
        ["TextPrimary"] = "TextPrimaryBrush",
        ["TextSecondary"] = "TextSecondaryBrush",
        ["TextMuted"] = "TextMutedBrush",
        ["Accent"] = "AccentBrush",
        ["AccentSoft"] = "AccentSubtleBrush",
        ["GameAccent"] = "SuccessBrush",
        ["Danger"] = "DangerBrush",
        ["MenuSurface"] = "MenuBrush",
        ["MenuOutline"] = "MenuBorderBrush",
    };

    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.Ordinal);
    private static bool _initialized;

    public static bool IsDark { get; private set; } = true;

    public static bool? ForcedDarkMode => Environment.GetEnvironmentVariable("FEATHER_THEME")?.ToLowerInvariant() switch
    {
        "dark" => true,
        "light" => false,
        _ => null
    };

    public static event EventHandler? ThemeChanged;

    public static void Initialize(Application application)
    {
        if (_initialized)
            return;

        _initialized = true;
        IsDark = !SystemUsesLightTheme();

        foreach ((string key, (string dark, string light)) in Palette)
        {
            var brush = new SolidColorBrush(Parse(IsDark ? dark : light));
            Brushes[key] = brush;
            application.Resources[key] = brush;
        }

        foreach ((string alias, string key) in Aliases)
            application.Resources[alias] = Brushes[key];

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                application.Dispatcher.BeginInvoke(Refresh);
        };
    }

    public static SolidColorBrush Brush(string key) => Brushes[key];

    public static Color ColorOf(string key) => Brushes[key].Color;

    public static void Refresh()
    {
        bool dark = !SystemUsesLightTheme();
        if (dark == IsDark)
            return;

        IsDark = dark;
        foreach ((string key, (string darkValue, string lightValue)) in Palette)
            Brushes[key].Color = Parse(dark ? darkValue : lightValue);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool SystemUsesLightTheme()
    {
        if (ForcedDarkMode is bool forcedDark)
            return !forcedDark;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
