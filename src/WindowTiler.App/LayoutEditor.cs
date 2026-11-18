using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WindowTiler.App;

internal sealed class LayoutEditor : Window
{
    private readonly LayoutNode root;
    private readonly string templateId;
    private string selected;
    private readonly Canvas canvas = new() { Background = Brushes.Transparent, ClipToBounds = true, MinHeight = 260 };
    private readonly TextBox name = new();
    private readonly TextBox percent = new() { Width = 60, Text = "50" };
    private readonly TextBlock hint = new() { Foreground = ThemeService.Brush("TextSecondary"), TextWrapping = TextWrapping.Wrap };
    internal LayoutTemplate? Result { get; private set; }

    internal LayoutEditor(LayoutTemplate? template)
    {
        Style = (Style)FindResource(typeof(Window));
        ThemeService.Track(this);
        Title = template is null ? "新建自定义布局" : "编辑自定义布局";
        Width = 820; Height = 650; MinWidth = 680; MinHeight = 540; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        root = template?.Root.Clone() ?? new LayoutNode { Axis = "X", Ratio = .6, First = new(), Second = new() { Axis = "Y", First = new(), Second = new() } };
        templateId = template?.Id ?? Guid.NewGuid().ToString("N"); selected = root.Leaves().First().Id;
        var panel = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock { Text = "设计你的屏幕分区", FontSize = 23, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "选择分区后拆分，拖动分界线调整比例。窗口将按编号依次填入。", Foreground = ThemeService.Brush("TextSecondary"), Margin = new Thickness(0, 8, 0, 14) });
        name.Text = template?.Name ?? "我的布局"; name.MaxLength = 40; name.ToolTip = "模板名称"; header.Children.Add(name);
        var toolbar = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        AddButton(toolbar, "左右拆分", () => SplitSelected("X")); AddButton(toolbar, "上下拆分", () => SplitSelected("Y")); AddButton(toolbar, "合并到上级", Merge);
        header.Children.Add(toolbar);
        var ratio = new WrapPanel { Margin = new Thickness(0, 12, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ratio.Children.Add(new TextBlock { Text = "上级分界线位置（%）", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        ratio.Children.Add(percent); AddButton(ratio, "应用比例", ApplyPercent); header.Children.Add(ratio);
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var save = new Button { Content = "保存模板", Style = (Style)FindResource("PrimaryButton"), IsDefault = true, Margin = new Thickness(12, 0, 0, 0) };
        save.Click += (_, _) => Save(); DockPanel.SetDock(save, Dock.Right); footer.Children.Add(save);
        var cancel = new Button { Content = "取消", IsCancel = true }; DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel); footer.Children.Add(hint);
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        panel.Children.Add(new Border { Background = ThemeService.Brush("Surface"), BorderBrush = ThemeService.Brush("ZoneBorder"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = canvas });
        Content = panel; canvas.SizeChanged += (_, _) => Draw(); Loaded += (_, _) => { SyncPercent(); Draw(); };
    }
    private static void AddButton(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label }; button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    private void SplitSelected(string axis)
    {
        if (root.Leaves().Count() >= 16) { hint.Text = "最多支持 16 个分区。"; return; }
        var node = root.Find(selected)!; node.Axis = axis; node.Ratio = .5; node.First = new(); node.Second = new(); selected = node.First.Id; SyncPercent(); Draw();
    }
    private void Merge()
    {
        var parent = root.ParentOf(selected);
        if (parent is null) { hint.Text = "已经是整个屏幕。"; return; }
        parent.First = parent.Second = null; selected = parent.Id; SyncPercent(); Draw();
    }
    private void ApplyPercent()
    {
        var parent = root.ParentOf(selected);
        if (parent is null) { hint.Text = "先拆分屏幕，再调整分界线。"; return; }
        if (!double.TryParse(percent.Text, out double value) || !double.IsFinite(value) || value is < 10 or > 90) { hint.Text = "请输入 10 到 90 之间的比例。"; return; }
        parent.Ratio = value / 100; Draw();
    }
    private void SyncPercent() => percent.Text = Math.Round((root.ParentOf(selected)?.Ratio ?? .5) * 100).ToString();
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(name.Text)) { hint.Text = "请填写模板名称。"; return; }
        if (root.ParentOf(selected) is { } parent)
        {
            if (!double.TryParse(percent.Text, out double value) || !double.IsFinite(value) || value is < 10 or > 90) { hint.Text = "请输入 10 到 90 之间的比例。"; return; }
            parent.Ratio = value / 100;
        }
        root.Validate(); Result = new() { Id = templateId, Name = name.Text.Trim(), Root = root.Clone() }; DialogResult = true;
    }
    private PixelRect Area => new(0, 0, Math.Max(1, (int)canvas.ActualWidth), Math.Max(1, (int)canvas.ActualHeight));
    private void Draw(bool handles = true)
    {
        if (canvas.ActualWidth < 1) return;
        if (handles) canvas.Children.Clear();
        else foreach (var element in canvas.Children.OfType<Border>().ToList()) canvas.Children.Remove(element);
        int index = 1;
        foreach (var (node, rect) in LayoutEngine.Partition(root, Area))
        {
            bool active = node.Id == selected;
            var border = new Border { Width = Math.Max(1, rect.Width - 6), Height = Math.Max(1, rect.Height - 6), Background = ThemeService.Brush(active ? "ZonePrimary" : "ZoneSecondary"), BorderBrush = active ? (Brush)FindResource("Accent") : ThemeService.Brush("ZoneBorder"), BorderThickness = new Thickness(active ? 2 : 1), CornerRadius = new CornerRadius(5), Cursor = Cursors.Hand,
                Child = new TextBlock { Text = $"{index++}", FontSize = 23, Foreground = active ? (Brush)FindResource("Accent") : ThemeService.Brush("TextSecondary"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            border.MouseLeftButtonDown += (_, _) => { selected = node.Id; SyncPercent(); Draw(); };
            Canvas.SetLeft(border, rect.X + 3); Canvas.SetTop(border, rect.Y + 3); Panel.SetZIndex(border, 0); canvas.Children.Add(border);
        }
        if (handles) DrawHandles(root, Area);
        hint.Text = $"{root.Leaves().Count()} 个分区 · 蓝色为选中分区 · 拖动分界线调整";
    }
    private void DrawHandles(LayoutNode node, PixelRect rect)
    {
        if (node.IsLeaf) return;
        bool horizontal = node.Axis == "X";
        var parts = LayoutEngine.Split(rect, horizontal, node.Ratio);
        var thumb = new Thumb { Width = horizontal ? 8 : rect.Width, Height = horizontal ? rect.Height : 8, Background = Brushes.Transparent, Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS, Opacity = .5 };
        var factory = new FrameworkElementFactory(typeof(Border)); factory.SetValue(Border.BackgroundProperty, ThemeService.Brush("ZoneBorder")); factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3)); thumb.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
        Canvas.SetLeft(thumb, horizontal ? parts.First.Right - 4 : rect.X); Canvas.SetTop(thumb, horizontal ? rect.Y : parts.First.Bottom - 4); Panel.SetZIndex(thumb, 5);
        double startRatio = 0, pointerStart = 0;
        thumb.DragStarted += (_, _) => { startRatio = node.Ratio; var p = Mouse.GetPosition(canvas); pointerStart = horizontal ? p.X : p.Y; };
        thumb.DragDelta += (_, _) =>
        {
            var p = Mouse.GetPosition(canvas);
            node.Ratio = Math.Clamp(startRatio + ((horizontal ? p.X : p.Y) - pointerStart) / (horizontal ? rect.Width : rect.Height), .1, .9);
            var split = LayoutEngine.Split(rect, horizontal, node.Ratio);
            if (horizontal) Canvas.SetLeft(thumb, split.First.Right - 4); else Canvas.SetTop(thumb, split.First.Bottom - 4);
            Draw(false); SyncPercent();
        };
        thumb.DragCompleted += (_, _) => Draw();
        canvas.Children.Add(thumb); DrawHandles(node.First!, parts.First); DrawHandles(node.Second!, parts.Second);
    }
}
