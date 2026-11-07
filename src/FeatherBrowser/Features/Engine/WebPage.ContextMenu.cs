using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CefSharp;

namespace FeatherBrowser.Features.Engine;

internal sealed partial class WebPage
{
    private sealed record MenuEntry(CefMenuCommand Command, string Label, MenuItemType Type, bool Enabled, bool Checked, IReadOnlyList<MenuEntry> Children);

    private static List<MenuEntry> ReadMenu(IMenuModel model, int depth = 0)
    {
        var entries = new List<MenuEntry>();
        for (int i = 0; i < model.Count; i++)
        {
            if (!model.IsVisibleAt(i))
                continue;

            MenuItemType type = model.GetTypeAt(i);
            IReadOnlyList<MenuEntry> children = type == MenuItemType.SubMenu && depth < 3 && model.GetSubMenuAt(i) is { } sub
                ? ReadMenu(sub, depth + 1)
                : [];

            entries.Add(new MenuEntry(
                model.GetCommandIdAt(i),
                CleanLabel(model.GetLabelAt(i)),
                type,
                model.IsEnabledAt(i),
                model.IsCheckedAt(i),
                children));
        }

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            bool separator = entries[i].Type == MenuItemType.Separator;
            if (separator && (i == 0 || i == entries.Count - 1 || entries[i - 1].Type == MenuItemType.Separator))
                entries.RemoveAt(i);
        }

        return entries;
    }

    private static string CleanLabel(string? label)
    {
        if (string.IsNullOrEmpty(label))
            return string.Empty;

        return label.Replace("&&", "\u0001").Replace("_", "__").Replace("&", "_").Replace("\u0001", "&");
    }

    private void ShowContextMenu(IReadOnlyList<MenuEntry> entries, IRunContextMenuCallback callback)
    {
        bool answered = false;
        void Answer(CefMenuCommand? command)
        {
            if (answered)
                return;
            answered = true;
            using (callback)
            {
                if (command is { } id)
                    callback.Continue(id, CefEventFlags.None);
                else
                    callback.Cancel();
            }
        }

        if (_disposed || _browser is null || entries.Count == 0)
        {
            Answer(null);
            return;
        }

        var menu = new ContextMenu { MinWidth = 220 };
        foreach (object item in BuildItems(entries, command => Answer(command)))
            menu.Items.Add(item);

        Point cursor = CursorInDips(_browser);
        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = cursor.X;
        menu.VerticalOffset = cursor.Y;
        menu.PlacementTarget = _browser;

        menu.Closed += (_, _) => _dispatcher.BeginInvoke(DispatcherPriority.Input, () => Answer(null));
        menu.IsOpen = true;
    }

    private static IEnumerable<object> BuildItems(IReadOnlyList<MenuEntry> entries, Action<CefMenuCommand> choose)
    {
        foreach (MenuEntry entry in entries)
        {
            if (entry.Type == MenuItemType.Separator)
            {
                yield return new Separator();
                continue;
            }

            var item = new MenuItem
            {
                Header = entry.Label,
                IsEnabled = entry.Enabled,
                IsCheckable = entry.Type is MenuItemType.Check or MenuItemType.Radio,
                IsChecked = entry.Checked,
            };

            if (entry.Type == MenuItemType.SubMenu)
            {
                foreach (object child in BuildItems(entry.Children, choose))
                    item.Items.Add(child);
            }
            else
            {
                CefMenuCommand command = entry.Command;
                item.Click += (_, _) => choose(command);
            }

            yield return item;
        }
    }

    private static Point CursorInDips(Visual relativeTo)
    {
        if (!GetCursorPos(out NativePoint point))
            return new Point(0, 0);

        DpiScale dpi = VisualTreeHelper.GetDpi(relativeTo);
        return new Point(point.X / dpi.DpiScaleX, point.Y / dpi.DpiScaleY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
