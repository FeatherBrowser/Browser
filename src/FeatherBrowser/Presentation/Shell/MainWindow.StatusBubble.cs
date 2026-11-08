using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private static readonly TimeSpan StatusMessageDuration = TimeSpan.FromSeconds(4);

    private readonly TextBlock StatusText = new()
    {
        FontSize = 11.5,
        MaxWidth = 560,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly DispatcherTimer _statusHideTimer = new() { Interval = StatusMessageDuration };
    private Popup? _statusPopup;
    private Border? _statusBubble;
    private bool _statusIsLinkHover;

    private void InitializeStatusBubble()
    {
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        _statusBubble = new Border
        {
            Child = StatusText,
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false
        };
        _statusBubble.SetResourceReference(Border.BackgroundProperty, "ChromeBrush");
        _statusBubble.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");

        _statusPopup = new Popup
        {
            Child = _statusBubble,
            AllowsTransparency = true,
            Focusable = false,
            IsHitTestVisible = false,
            PlacementTarget = BrowserHost,
            Placement = PlacementMode.Relative,
            PopupAnimation = PopupAnimation.None
        };

        DependencyPropertyDescriptor
            .FromProperty(TextBlock.TextProperty, typeof(TextBlock))
            .AddValueChanged(StatusText, (_, _) => OnStatusTextChanged());

        _statusHideTimer.Tick += (_, _) => HideStatusBubble();
        Deactivated += (_, _) => HideStatusBubble();
        StateChanged += (_, _) => HideStatusBubble();
        LocationChanged += (_, _) => HideStatusBubble();
        BrowserHost.SizeChanged += (_, _) => PositionStatusBubble();
    }

    private void SetLinkHoverStatus(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            if (_statusIsLinkHover)
                HideStatusBubble();
            return;
        }

        _statusIsLinkHover = true;
        StatusText.Text = address;
    }

    private void OnStatusTextChanged()
    {
        if (_statusPopup is null)
            return;

        _statusHideTimer.Stop();
        if (string.IsNullOrWhiteSpace(StatusText.Text) || !IsActive || WindowState == WindowState.Minimized)
        {
            _statusPopup.IsOpen = false;
            return;
        }

        PositionStatusBubble();
        _statusPopup.IsOpen = true;
        if (!_statusIsLinkHover)
            _statusHideTimer.Start();
    }

    private void HideStatusBubble()
    {
        _statusHideTimer.Stop();
        _statusIsLinkHover = false;
        if (_statusPopup is not null)
            _statusPopup.IsOpen = false;

        if (StatusText.Text.Length > 0)
            StatusText.Text = string.Empty;
    }

    private void PositionStatusBubble()
    {
        if (_statusPopup is null || _statusBubble is null)
            return;

        _statusBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _statusPopup.HorizontalOffset = 6;
        _statusPopup.VerticalOffset = Math.Max(0, BrowserHost.ActualHeight - _statusBubble.DesiredSize.Height - 6);
    }
}
