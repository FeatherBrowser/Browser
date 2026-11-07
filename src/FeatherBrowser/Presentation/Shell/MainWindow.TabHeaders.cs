using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private const double TabMinWidth = 60;
    private const double TabMaxWidth = 210;
    private const double TabGap = 2;

    private (Border Header, TextBlock Title, TextBlock State, Button CloseButton) CreateTabHeader()
    {
        var favicon = new Image
        {
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            Visibility = Visibility.Collapsed
        };
        RenderOptions.SetBitmapScalingMode(favicon, BitmapScalingMode.HighQuality);

        FrameworkElement fallbackIcon = CreateDefaultTabIcon();
        FrameworkElement spinner = CreateTabSpinner();

        var iconHost = new Grid
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        iconHost.Children.Add(fallbackIcon);
        iconHost.Children.Add(favicon);
        iconHost.Children.Add(spinner);

        var title = new TextBlock
        {
            Text = "New Tab",
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var state = new TextBlock
        {
            Text = string.Empty,
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 10,
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand
        };
        state.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var closeButton = new Button
        {
            Content = "",
            Style = (Style)FindResource("IconButton"),
            Width = 20,
            Height = 20,
            FontSize = 8,
            Margin = new Thickness(3, 0, 0, 0),
            ToolTip = "Close tab (Ctrl+W)",
            Visibility = Visibility.Hidden
        };

        var content = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        content.Children.Add(iconHost);
        Grid.SetColumn(title, 1);
        content.Children.Add(title);
        Grid.SetColumn(state, 2);
        content.Children.Add(state);
        Grid.SetColumn(closeButton, 3);
        content.Children.Add(closeButton);

        var header = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, TabGap, 0),
            Padding = new Thickness(8, 0, 3, 0),
            Width = TabMaxWidth,
            Height = 26,
            Child = content,
            SnapsToDevicePixels = true,
            Tag = new TabHeaderVisuals
            {
                Favicon = favicon,
                FallbackIcon = fallbackIcon,
                Spinner = spinner,
                CloseButton = closeButton,
                Title = title
            }
        };
        WindowChrome.SetIsHitTestVisibleInChrome(header, true);

        header.MouseEnter += (_, _) => RefreshTabHeader(header);
        header.MouseLeave += (_, _) => RefreshTabHeader(header);

        return (header, title, state, closeButton);
    }

    private void RefreshTabHeader(Border header)
    {
        if (header.Tag is not TabHeaderVisuals visuals)
            return;

        bool active = _activeTab?.Header == header;
        bool hover = header.IsMouseOver;

        if (active)
            header.SetResourceReference(Border.BackgroundProperty, "TabActiveBrush");
        else if (hover)
            header.SetResourceReference(Border.BackgroundProperty, "TabHoverBrush");
        else
            header.Background = Brushes.Transparent;

        visuals.Title.SetResourceReference(TextBlock.ForegroundProperty, active ? "TextPrimaryBrush" : "TextSecondaryBrush");
        bool roomForClose = header.ActualWidth <= 0 || header.ActualWidth >= 88 || active;
        visuals.CloseButton.Visibility = (active || hover) && roomForClose ? Visibility.Visible : Visibility.Hidden;
    }

    private FrameworkElement CreateTabSpinner()
    {
        var arc = new Path
        {
            Width = 12,
            Height = 12,
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M 6,1 A 5,5 0 1 1 1,6"),
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        arc.SetResourceReference(Shape.StrokeProperty, "AccentBrush");

        var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.8)))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        arc.IsVisibleChanged += (_, e) =>
        {
            var rotate = (RotateTransform)arc.RenderTransform;
            rotate.BeginAnimation(RotateTransform.AngleProperty, (bool)e.NewValue ? spin : null);
        };
        return arc;
    }

    private static void SetTabLoading(BrowserTab tab, bool loading)
    {
        if (tab.Header.Tag is not TabHeaderVisuals visuals)
            return;

        visuals.Spinner.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        visuals.Favicon.Opacity = loading ? 0 : 1;
        visuals.FallbackIcon.Opacity = loading ? 0 : 1;
    }

    private FrameworkElement CreateDefaultTabIcon()
    {
        var icon = new TextBlock
        {
            Text = "",
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        return icon;
    }

    private void AttachTabHeaderEvents(BrowserTab tab, Border header, Button closeButton)
    {
        header.MouseDown += async (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                e.Handled = true;
                await CloseTabAsync(tab);
                return;
            }

            if (e.ChangedButton == MouseButton.Left && !closeButton.IsMouseOver)
                await SelectTabAsync(tab);
        };

        header.MouseRightButtonUp += (_, _) =>
            BuildTabContextMenu(tab).IsOpen = true;

        closeButton.Click += async (_, _) =>
            await CloseTabAsync(tab);

        tab.StateIndicator.MouseLeftButtonDown += (_, e) =>
        {
            if (!tab.IsPlayingAudio && !tab.UserMuted)
                return;

            e.Handled = true;
            tab.UserMuted = !tab.UserMuted;
            ApplyTabMute(tab);
            StatusText.Text = tab.UserMuted ? $"Muted {TrimLabel(tab.Title.Text, 40)}" : $"Unmuted {TrimLabel(tab.Title.Text, 40)}";
        };
    }

    private void ApplyTabMute(BrowserTab tab)
    {
        bool background = tab != _activeTab || WindowState == WindowState.Minimized;
        tab.View.IsMuted = tab.UserMuted ||
            (background && (_store.Settings.MuteBackgroundTabs || _store.Settings.GameMode));
        tab.UpdateStateIndicator();
    }

    private void UpdateTabSelection(BrowserTab selectedTab)
    {
        foreach (BrowserTab item in _tabs)
        {
            item.SetSelected(item == selectedTab);
            RefreshTabHeader(item.Header);
        }
    }

    private void TabBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTabStripLayout();
    }

    private void UpdateTabStripLayout()
    {
        if (TabBar is null || NewTabButton is null || TabStrip.Parent is not ScrollViewer tabScrollViewer)
            return;

        double reserved = NewTabButton.Width + NewTabButton.Margin.Left + NewTabButton.Margin.Right;
        double available = Math.Max(0, TabBar.ActualWidth - TabBar.Padding.Left - TabBar.Padding.Right - reserved);
        tabScrollViewer.MaxWidth = available;

        int visible = 0;
        foreach (UIElement child in TabStrip.Children)
        {
            if (child.Visibility == Visibility.Visible)
                visible++;
        }

        if (visible == 0)
            return;

        double width = Math.Clamp(Math.Floor(available / visible) - TabGap, TabMinWidth, TabMaxWidth);
        foreach (UIElement child in TabStrip.Children)
        {
            if (child is Border header)
            {
                header.Width = width;
                RefreshTabHeader(header);
            }
        }
    }
}
