using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace YetAnotherOneCLauncher.App.Controls;

/// <summary>Перенос меток внутри одного списка с указателем места вставки.</summary>
internal static class TagReordering
{
    public static void Attach<T>(Control host, bool horizontal, Action<T, T, bool> move) where T : class
    {
        PointerPressedEventArgs? start = null;
        T? source = null;
        AdornerLayer? markerLayerHost = null;
        var markerLayer = new Border { Background = Brushes.DodgerBlue, IsHitTestVisible = false };
        Control? Find(object? origin) => (origin as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .FirstOrDefault(c => c.DataContext is T && (c is Button || c is ListBoxItem));
        void ClearMarker()
        {
            markerLayerHost?.Children.Remove(markerLayer);
            markerLayerHost = null;
        }
        host.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            start = e.GetCurrentPoint(host).Properties.IsLeftButtonPressed ? e : null;
            source = Find(e.Source)?.DataContext as T;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerReleasedEvent, (_, _) => start = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerMovedEvent, async (_, e) =>
        {
            if (start is not { } pressed || source is null || !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
            var delta = e.GetPosition(host) - pressed.GetPosition(host);
            if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6) return;
            start = null;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText("launcher-tag"));
            try { await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move); }
            finally { ClearMarker(); source = null; }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        DragDrop.SetAllowDrop(host, true);
        host.AddHandler(DragDrop.DragLeaveEvent, (_, _) => ClearMarker());
        host.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            ClearMarker();
            var target = Find(e.Source);
            e.DragEffects = source is not null && target?.DataContext is T item && !ReferenceEquals(source, item)
                ? DragDropEffects.Move : DragDropEffects.None;
            if (e.DragEffects == DragDropEffects.Move && target is not null)
            {
                var after = horizontal ? e.GetPosition(target).X > target.Bounds.Width / 2 : e.GetPosition(target).Y > target.Bounds.Height / 2;
                var layer = AdornerLayer.GetAdornerLayer(target);
                if (layer is not null)
                {
                    AdornerLayer.SetAdornedElement(markerLayer, target);
                    markerLayer.Width = horizontal ? 3 : target.Bounds.Width;
                    markerLayer.Height = horizontal ? target.Bounds.Height : 3;
                    markerLayer.HorizontalAlignment = horizontal && after ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left;
                    markerLayer.VerticalAlignment = !horizontal && after ? Avalonia.Layout.VerticalAlignment.Bottom : Avalonia.Layout.VerticalAlignment.Top;
                    layer.Children.Add(markerLayer);
                    markerLayerHost = layer;
                }
            }
            e.Handled = source is not null;
        });
        host.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            ClearMarker();
            var target = Find(e.Source);
            if (source is not null && target?.DataContext is T item && !ReferenceEquals(source, item))
            {
                var after = horizontal ? e.GetPosition(target).X > target.Bounds.Width / 2 : e.GetPosition(target).Y > target.Bounds.Height / 2;
                move(source, item, after);
                e.Handled = true;
            }
        });
    }
}
