using System.Windows.Input;

namespace WindowTiler.App;

public partial class MainWindow
{
    private Border? previewDragSource, previewDropTarget;
    private Point previewDragStart;
    private bool previewDragging;

    private void InitializePreviewDrag()
    {
        Preview.Focusable = true;
        Preview.Background = Brushes.Transparent;
        Preview.MouseLeftButtonDown += (_, e) =>
        {
            if (BeginPreviewDrag(e.GetPosition(Preview)))
            {
                Preview.Focus();
                if (!Preview.CaptureMouse()) CancelPreviewDrag();
                e.Handled = true;
            }
        };
        Preview.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) UpdatePreviewDrag(e.GetPosition(Preview));
            else CancelPreviewDrag();
        };
        Preview.MouseLeftButtonUp += (_, e) =>
        {
            if (previewDragSource is null) return;
            CompletePreviewDrag(e.GetPosition(Preview));
            e.Handled = true;
        };
        Preview.LostMouseCapture += (_, _) => CancelPreviewDrag();
        Preview.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && previewDragSource is not null)
            {
                CancelPreviewDrag(); e.Handled = true;
            }
        };
        Deactivated += (_, _) => CancelPreviewDrag();
    }

    private Border? PreviewZoneAt(Point point) => Preview.Children.OfType<Border>().FirstOrDefault(zone =>
        zone.Tag is WindowItem && new Rect(Canvas.GetLeft(zone), Canvas.GetTop(zone), zone.Width, zone.Height).Contains(point));

    internal bool BeginPreviewDrag(Point point)
    {
        CancelPreviewDrag();
        if (Manager.Busy || refreshing || PreviewZoneAt(point) is not { } source) return false;
        previewDragSource = source;
        previewDragStart = point;
        return true;
    }

    internal void UpdatePreviewDrag(Point point)
    {
        if (previewDragSource is null) return;
        if (Manager.Busy || refreshing) { CancelPreviewDrag(); return; }
        if (!previewDragging && Math.Abs(point.X - previewDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - previewDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        previewDragging = true;
        previewDragSource.Opacity = .55;
        Preview.Cursor = Cursors.SizeAll;
        if (previewDropTarget is not null)
        {
            previewDropTarget.BorderBrush = ThemeService.Brush("ZoneBorder");
            previewDropTarget.BorderThickness = new Thickness(1);
        }
        var target = PreviewZoneAt(point);
        previewDropTarget = ReferenceEquals(target, previewDragSource) ? null : target;
        if (previewDropTarget is not null)
        {
            previewDropTarget.BorderBrush = ThemeService.Brush("Accent");
            previewDropTarget.BorderThickness = new Thickness(3);
        }
    }

    internal bool CompletePreviewDrag(Point point)
    {
        UpdatePreviewDrag(point);
        var source = previewDragSource?.Tag as WindowItem;
        var target = previewDragging ? previewDropTarget?.Tag as WindowItem : null;
        CancelPreviewDrag();
        if (Manager.Busy || refreshing || source is null || target is null || source == target ||
            !source.IsSelected || !target.IsSelected || !Windows.Contains(source) || !Windows.Contains(target) ||
            (Settings.GroupByDisplay && (source.DisplayKey != ActiveDisplay?.Key || target.DisplayKey != ActiveDisplay?.Key))) return false;
        // Exchange exactly these two slots. All other windows retain their order
        // and monitor assignment, including unchecked and other-monitor rows.
        int sourceIndex = Windows.IndexOf(source), targetIndex = Windows.IndexOf(target);
        int lower = Math.Min(sourceIndex, targetIndex), upper = Math.Max(sourceIndex, targetIndex);
        // Move notifications keep WPF's collection view consistent; replacing two
        // entries would temporarily duplicate an item and confuse the bound grid.
        Windows.Move(upper, lower);
        if (upper > lower + 1) Windows.Move(lower + 1, upper);
        WindowGrid.SelectedItem = source;
        source.Result = target.Result = "位置已交换，待平铺";
        RenderPreview();
        Status("已交换两个窗口的位置，点击平铺后生效。");
        return true;
    }

    internal void CancelPreviewDrag()
    {
        if (previewDragSource is not null) previewDragSource.Opacity = 1;
        if (previewDropTarget is not null)
        {
            previewDropTarget.BorderBrush = ThemeService.Brush("ZoneBorder");
            previewDropTarget.BorderThickness = new Thickness(1);
        }
        previewDragSource = previewDropTarget = null;
        previewDragging = false;
        Preview.ClearValue(CursorProperty);
        if (Preview.IsMouseCaptured) Preview.ReleaseMouseCapture();
    }
}
