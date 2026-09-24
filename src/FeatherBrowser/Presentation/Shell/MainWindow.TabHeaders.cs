using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FeatherBrowser.Presentation.Tabs;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private static readonly Brush TabActiveBrush = CreateFrozenTabBrush(40, 60, 84);
    private static readonly Brush TabInactiveBrush = CreateFrozenTabBrush(10, 20, 34);
    private static readonly Brush TabHoverBrush = CreateFrozenTabBrush(20, 37, 55);
    private static readonly Brush TabHoverBorderBrush = CreateFrozenTabBrush(49, 74, 99);
    private static readonly Brush TabTitleBrush = CreateFrozenTabBrush(226, 232, 240);
    private static readonly Brush TabMutedTitleBrush = CreateFrozenTabBrush(156, 166, 181);

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

        FrameworkElement fallbackIcon = CreateDefaultTabIcon();

        var iconHost = new Grid
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        iconHost.Children.Add(fallbackIcon);
        iconHost.Children.Add(favicon);

        var title = new TextBlock
        {
            Text = "New Tab",
            Foreground = TabTitleBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12.5,
            FontWeight = FontWeights.Normal,
            MaxWidth = 160
        };

        var state = new TextBlock
        {
            Text = "○",
            Visibility = Visibility.Collapsed,
            Width = 0,
            Height = 0,
            IsHitTestVisible = false
        };

        var closeButton = new Button
        {
            Content = "×",
            Style = (Style)FindResource("IconButton"),
            Background = Brushes.Transparent,
            Foreground = TabMutedTitleBrush,
            BorderThickness = new Thickness(0),
            Width = 26,
            Height = 26,
            MinWidth = 26,
            MinHeight = 26,
            FontSize = 16,
            FontWeight = FontWeights.Light,
            Padding = new Thickness(0, 0, 0, 2),
            Margin = new Thickness(5, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Close tab",
            Focusable = false
        };

        var content = new Grid
        {
            VerticalAlignment = VerticalAlignment.Stretch
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        content.Children.Add(iconHost);

        Grid.SetColumn(title, 1);
        content.Children.Add(title);

        Grid.SetColumn(closeButton, 2);
        content.Children.Add(closeButton);

        var header = new Border
        {
            Background = TabInactiveBrush,
            CornerRadius = new CornerRadius(9, 9, 0, 0),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(1, 4, 1, 0),
            Padding = new Thickness(11, 0, 5, 0),
            MinWidth = 140,
            MaxWidth = 220,
            Height = 36,
            Child = content,
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
            Tag = new TabHeaderVisuals
            {
                Favicon = favicon,
                FallbackIcon = fallbackIcon
            }
        };

        header.MouseEnter += (_, _) =>
        {
            if (_activeTab?.Header != header)
            {
                header.Background = TabHoverBrush;
                header.BorderBrush = TabHoverBorderBrush;
            }
        };

        header.MouseLeave += (_, _) =>
        {
            if (_activeTab?.Header != header)
            {
                header.Background = TabInactiveBrush;
                header.BorderBrush = Brushes.Transparent;
            }
        };

        closeButton.MouseEnter += (_, _) =>
            closeButton.Foreground = Brushes.White;

        closeButton.MouseLeave += (_, _) =>
            closeButton.Foreground = TabMutedTitleBrush;

        return (header, title, state, closeButton);
    }

    private static readonly ImageSource DefaultTabIcon = CreateDefaultTabIconSource();

    private static FrameworkElement CreateDefaultTabIcon() => new Image
    {
        Width = 16,
        Height = 16,
        Source = DefaultTabIcon,
        Stretch = Stretch.Uniform,
        IsHitTestVisible = false
    };

    private static ImageSource CreateDefaultTabIconSource()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(null, new Pen(TabMutedTitleBrush, 1.25),
            new EllipseGeometry(new Point(8, 8), 6.5, 6.5)));
        drawing.Children.Add(new GeometryDrawing(null, new Pen(TabMutedTitleBrush, 1),
            new EllipseGeometry(new Point(8, 8), 3, 6.5)));
        drawing.Children.Add(new GeometryDrawing(null, new Pen(TabMutedTitleBrush, 1),
            new LineGeometry(new Point(2, 8), new Point(14, 8))));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
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
    }

    private void UpdateTabSelection(BrowserTab selectedTab)
    {
        foreach (BrowserTab item in _tabs)
        {
            bool selected = item == selectedTab;

            item.SetSelected(
                selected,
                TabActiveBrush,
                TabInactiveBrush,
                _accentBrush);

            item.Header.Background = selected ? TabActiveBrush : TabInactiveBrush;
            item.Header.BorderBrush = Brushes.Transparent;
            item.Header.BorderThickness = new Thickness(0);
            item.Title.Foreground = selected ? TabTitleBrush : TabMutedTitleBrush;
        }
    }

    private void TabBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTabStripLayout();
    }

    private void UpdateTabStripLayout()
    {
        if (TabBar is null || NewTabButton is null)
            return;

        if (TabStrip.Parent is not ScrollViewer tabScrollViewer)
            return;

        double reservedWidth =
            NewTabButton.ActualWidth +
            NewTabButton.Margin.Left +
            NewTabButton.Margin.Right;

        double horizontalPadding =
            TabBar.Padding.Left +
            TabBar.Padding.Right;

        double availableWidth = Math.Max(
            0,
            TabBar.ActualWidth - horizontalPadding - reservedWidth
        );

        tabScrollViewer.MaxWidth = availableWidth;
    }

    private static Brush CreateFrozenTabBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
