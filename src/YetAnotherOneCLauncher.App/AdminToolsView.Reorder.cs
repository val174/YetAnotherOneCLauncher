using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App;

/// <summary>
/// Ручной порядок инструментов: перетаскивание строки мышью (черта показывает, куда она встанет) и Alt+↑ / Alt+↓.
/// </summary>
public partial class AdminToolsView
{
    /// <summary>Сколько провести мышью с зажатой кнопкой, чтобы началось перетаскивание, а не просто выбор строки.</summary>
    private const double DragThreshold = 6;

    private AdminToolRow? _pressedRow;
    private Point _pressPoint;
    private bool _dragging;

    private void InitReorder()
    {
        // Туннелем и с обработанными: ListBox сам забирает нажатия (выбор строки) и стрелки.
        RowsList.AddHandler(PointerPressedEvent, OnRowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        RowsList.AddHandler(PointerMovedEvent, OnRowPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        RowsList.AddHandler(PointerReleasedEvent, OnRowPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        RowsList.PointerCaptureLost += (_, _) => EndDrag();
        RowsList.AddHandler(KeyDownEvent, OnRowsKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        if (Tools is not { } tools || e.KeyModifiers != KeyModifiers.Alt)
        {
            return;
        }

        var command = e.Key switch
        {
            Key.Up => tools.MoveUpCommand,
            Key.Down => tools.MoveDownCommand,
            _ => null,
        };
        if (command is not null)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
                // Переставленная строка получает новый элемент списка — фокус возвращается на неё, чтобы двигать дальше.
                var row = tools.SelectedRow!;
                RowsList.ScrollIntoView(row);
                Dispatcher.UIThread.Post(() => RowsList.ContainerFromItem(row)?.Focus(), DispatcherPriority.Loaded);
            }

            e.Handled = true;
        }
    }

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedRow = e.GetCurrentPoint(RowsList).Properties.IsLeftButtonPressed
            ? (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as AdminToolRow
            : null;
        _pressPoint = e.GetPosition(RowsList);
    }

    private void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedRow is null || !e.GetCurrentPoint(RowsList).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(RowsList);
        if (!_dragging)
        {
            var delta = point - _pressPoint;
            if (Math.Abs(delta.Y) < DragThreshold && Math.Abs(delta.X) < DragThreshold)
            {
                return;
            }

            _dragging = true;
            e.Pointer.Capture(RowsList);
        }

        ShowDropIndicator(DropBoundary(point.Y));
        e.Handled = true;
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _pressedRow is { } row && Tools is { } tools)
        {
            // Граница между строками → место строки: если строка уезжает вниз, сама она перед границей уже не считается.
            var boundary = DropBoundary(e.GetPosition(RowsList).Y);
            var from = tools.Rows.IndexOf(row);
            tools.MoveTo(row, boundary > from ? boundary - 1 : boundary);
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        EndDrag();
    }

    private void EndDrag()
    {
        _dragging = false;
        _pressedRow = null;
        DropIndicator.IsVisible = false;
    }

    /// <summary>Перед какой строкой встанет перетаскиваемая (по середине строк); после последней — число строк.</summary>
    private int DropBoundary(double y)
    {
        var count = RowsList.ItemCount;
        for (var i = 0; i < count; i++)
        {
            if (RowsList.ContainerFromIndex(i) is { } item && item.TranslatePoint(default, RowsList) is { } top
                && y < top.Y + item.Bounds.Height / 2)
            {
                return i;
            }
        }

        return count;
    }

    private void ShowDropIndicator(int boundary)
    {
        var count = RowsList.ItemCount;
        var item = RowsList.ContainerFromIndex(boundary < count ? boundary : count - 1);
        var host = (Visual)DropIndicator.GetVisualParent()!;
        if (item?.TranslatePoint(default, host) is not { } top)
        {
            DropIndicator.IsVisible = false;
            return;
        }

        var y = boundary < count ? top.Y : top.Y + item.Bounds.Height;
        DropIndicator.Margin = new Thickness(DropIndicator.Margin.Left, y - DropIndicator.Height / 2, DropIndicator.Margin.Right, 0);
        DropIndicator.IsVisible = true;
    }
}
