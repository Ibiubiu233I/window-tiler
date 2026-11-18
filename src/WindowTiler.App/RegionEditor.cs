using System.Windows.Input;
using System.Windows.Interop;

namespace WindowTiler.App;

internal sealed class RegionEditor : Window
{
    private readonly DisplayItem display;
    private readonly string id;
    internal readonly TextBox NameInput = new() { MaxLength = 40 };
    internal readonly TextBox[] Coordinates = Enumerable.Range(0, 4).Select(_ => new TextBox { MinWidth = 85 }).ToArray();
    internal readonly Canvas Screen = new() { Background = ThemeService.Brush("RegionScreen"), ClipToBounds = true, Cursor = Cursors.Cross };
    private readonly TextBlock areaSummary = new() { Foreground = ThemeService.Brush("TextSecondary"), FontSize = 11, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock hint = new() { Foreground = ThemeService.Brush("TextSecondary"), TextWrapping = TextWrapping.Wrap };
    private Point? start;
    private bool syncing;
    internal TilingRegion? Result { get; private set; }

    internal RegionEditor(DisplayItem display, TilingRegion? source = null, bool copy = false)
    {
        this.display = display;
        id = source is null || copy ? Guid.NewGuid().ToString("N") : source.Id;
        Style = (Style)FindResource(typeof(Window)); ThemeService.Track(this);
        Title = source is null ? "新建区域方案" : copy ? "复制区域方案" : "编辑区域方案";
        Width = 760; Height = 620; MinWidth = 600; MinHeight = 510; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        NameInput.Text = source is null ? "我的区域" : copy ? source.Name[..Math.Min(37, source.Name.Length)] + " 副本" : source.Name;
        var root = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "划定窗口平铺范围", FontSize = 23, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = $"{display.Label} · 可用范围 {display.Work.Width} × {display.Work.Height} px", Foreground = ThemeService.Brush("TextSecondary"), Margin = new Thickness(0, 8, 0, 14) });
        header.Children.Add(NameInput);
        var fields = new Grid { Margin = new Thickness(0, 14, 0, 14) };
        var labels = new[] { "距左侧（px）", "距顶部（px）", "宽度（px）", "高度（px）" };
        for (int i = 0; i < 4; i++)
        {
            fields.ColumnDefinitions.Add(new ColumnDefinition());
            var stack = new StackPanel { Margin = new Thickness(0, 0, i == 3 ? 0 : 12, 0) };
            stack.Children.Add(new TextBlock { Text = labels[i], Foreground = ThemeService.Brush("TextSecondary"), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
            stack.Children.Add(Coordinates[i]); Grid.SetColumn(stack, i); fields.Children.Add(stack);
            Coordinates[i].TextChanged += (_, _) => { if (!syncing) Draw(); };
        }
        header.Children.Add(fields);
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var select = new Button { Content = "在屏幕上框选" };
        select.Click += (_, _) =>
        {
            var overlay = new RegionSelectionOverlay(display) { Owner = this };
            if (overlay.ShowDialog() == true && overlay.Result is { } rect) SetRectangle(rect);
            Activate();
        };
        actions.Children.Add(select);
        var reset = new Button { Content = "填满可用范围" };
        reset.Click += (_, _) => SetRectangle(new(0, 0, display.Work.Width, display.Work.Height)); actions.Children.Add(reset);
        header.Children.Add(actions); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var save = new Button { Content = "保存方案", Style = (Style)FindResource("PrimaryButton"), IsDefault = true, Margin = new Thickness(12, 0, 0, 0) };
        save.Click += (_, _) => { if (TryCreateResult()) DialogResult = true; };
        DockPanel.SetDock(save, Dock.Right); footer.Children.Add(save);
        var cancel = new Button { Content = "取消", IsCancel = true }; DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel);
        footer.Children.Add(hint); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var preview = new DockPanel();
        var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (color, label) in new[] { ("RegionScreen", "屏幕总范围"), ("RegionSelection", "选中平铺区域") })
        {
            var entry = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 20, 0) };
            entry.Children.Add(new Border { Width = 12, Height = 12, Background = ThemeService.Brush(color), BorderBrush = ThemeService.Brush(color == "RegionScreen" ? "RegionScreenBorder" : "Accent"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 6, 0) });
            entry.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = ThemeService.Brush("TextSecondary") });
            legend.Children.Add(entry);
        }
        DockPanel.SetDock(legend, Dock.Top); preview.Children.Add(legend);
        DockPanel.SetDock(areaSummary, Dock.Bottom); preview.Children.Add(areaSummary);
        preview.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = Screen });
        Screen.Width = display.Work.Width; Screen.Height = display.Work.Height;
        root.Children.Add(new Border { Background = ThemeService.Brush("Surface"), BorderBrush = ThemeService.Brush("BorderSubtle"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = preview }); Content = root;
        Screen.MouseLeftButtonDown += (_, e) => { start = e.GetPosition(Screen); Screen.CaptureMouse(); e.Handled = true; };
        Screen.MouseMove += (_, e) => { if (start is { } p && e.LeftButton == MouseButtonState.Pressed) SelectRectangle(p, e.GetPosition(Screen)); };
        Screen.MouseLeftButtonUp += (_, e) => { if (start is { } p) SelectRectangle(p, e.GetPosition(Screen)); start = null; Screen.ReleaseMouseCapture(); };
        Screen.LostMouseCapture += (_, _) => start = null;
        SetRectangle(source is null ? new(display.Work.Width / 8, display.Work.Height / 8, display.Work.Width * 3 / 4, display.Work.Height * 3 / 4) : new(source.X, source.Y, source.Width, source.Height));
    }
    internal void SelectRectangle(Point from, Point to) => SetRectangle(RegionSelectionOverlay.RectangleFromPoints(from, to, display.Work));
    internal void SetRectangle(PixelRect rectangle)
    {
        syncing = true;
        var values = new[] { rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height };
        for (int i = 0; i < 4; i++) Coordinates[i].Text = values[i].ToString();
        syncing = false; Draw();
    }
    private TilingRegion ReadRegion()
    {
        var values = new int[4];
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(Coordinates[i].Text, out values[i])) throw new ArgumentException("位置和宽高请输入整数像素。");
        var region = new TilingRegion { Id = id, Name = NameInput.Text.Trim(), X = values[0], Y = values[1], Width = values[2], Height = values[3] };
        region.Resolve(display.Work); return region;
    }
    internal bool TryCreateResult()
    {
        try { Result = ReadRegion(); return true; }
        catch (ArgumentException ex) { hint.Text = ex.Message; return false; }
    }
    private void Draw()
    {
        Screen.Children.Clear();
        var screenOutline = new Border { Width = display.Work.Width, Height = display.Work.Height,
            BorderBrush = ThemeService.Brush("RegionScreenBorder"), BorderThickness = new Thickness(5), IsHitTestVisible = false };
        Panel.SetZIndex(screenOutline, 1); Screen.Children.Add(screenOutline);
        areaSummary.Text = $"屏幕总范围：{display.Work.Width} × {display.Work.Height} px";
        try
        {
            var region = ReadRegion();
            var selection = new Border { Width = region.Width, Height = region.Height, Background = ThemeService.Brush("RegionSelection"), BorderBrush = ThemeService.Brush("Accent"), BorderThickness = new Thickness(5), IsHitTestVisible = false };
            Canvas.SetLeft(selection, region.X); Canvas.SetTop(selection, region.Y); Screen.Children.Add(selection);
            areaSummary.Text += $"   ·   已选：{region.Width} × {region.Height} px（占 {100.0 * region.Width * region.Height / display.Work.Width / display.Work.Height:0.#}%）";
            hint.Text = "在预览或屏幕上拖出矩形，也可输入数值微调。位置相对可用范围左上角。";
        }
        catch (ArgumentException ex) { hint.Text = ex.Message; }
    }
}

internal sealed class RegionSelectionOverlay : Window
{
    private readonly Canvas canvas = new() { Background = new SolidColorBrush(Color.FromArgb(90, 10, 18, 35)) };
    private readonly Border selection = new() { Background = new SolidColorBrush(Color.FromArgb(70, 80, 145, 255)), BorderBrush = Brushes.White, BorderThickness = new Thickness(2), IsHitTestVisible = false };
    private readonly DisplayItem display;
    private Point? start;
    internal PixelRect? Result { get; private set; }
    internal RegionSelectionOverlay(DisplayItem display)
    {
        this.display = display;
        Title = "拖动划定平铺区域 · Esc 取消"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true;
        Cursor = Cursors.Cross; Content = canvas;
        canvas.Children.Add(selection);
        var tip = new Border { Background = new SolidColorBrush(Color.FromRgb(25, 36, 60)), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 10, 16, 10), IsHitTestVisible = false,
            Child = new TextBlock { Text = $"屏幕 {display.Number} · 按住鼠标拖出矩形，松手完成 · Esc 取消", Foreground = Brushes.White, FontSize = 16 } };
        Canvas.SetLeft(tip, 20); Canvas.SetTop(tip, 20); canvas.Children.Add(tip);
        Loaded += (_, _) =>
        {
            var work = display.Work;
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, -1, work.X, work.Y, work.Width, work.Height, 0x0040);
            Activate(); Focus();
        };
        canvas.MouseLeftButtonDown += (_, e) => { start = Position(e); canvas.CaptureMouse(); };
        canvas.MouseMove += (_, e) =>
        {
            if (start is not { } p) return;
            var rect = RectangleFromPoints(p, Position(e), display.Work);
            selection.Width = rect.Width * canvas.ActualWidth / display.Work.Width;
            selection.Height = rect.Height * canvas.ActualHeight / display.Work.Height;
            Canvas.SetLeft(selection, rect.X * canvas.ActualWidth / display.Work.Width);
            Canvas.SetTop(selection, rect.Y * canvas.ActualHeight / display.Work.Height);
        };
        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (start is not { } p) return;
            var rect = RectangleFromPoints(p, Position(e), display.Work);
            start = null; canvas.ReleaseMouseCapture();
            if (rect.Width < 8 || rect.Height < 8) return;
            Result = rect; DialogResult = true;
        };
        canvas.LostMouseCapture += (_, _) => start = null;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
    private Point Position(MouseEventArgs e)
    {
        var p = e.GetPosition(canvas);
        return new(p.X * display.Work.Width / Math.Max(1, canvas.ActualWidth), p.Y * display.Work.Height / Math.Max(1, canvas.ActualHeight));
    }
    internal static PixelRect RectangleFromPoints(Point from, Point to, PixelRect work)
    {
        int x1 = (int)Math.Clamp(Math.Round(Math.Min(from.X, to.X)), 0, work.Width - 1);
        int y1 = (int)Math.Clamp(Math.Round(Math.Min(from.Y, to.Y)), 0, work.Height - 1);
        int x2 = (int)Math.Clamp(Math.Round(Math.Max(from.X, to.X)), x1 + 1, work.Width);
        int y2 = (int)Math.Clamp(Math.Round(Math.Max(from.Y, to.Y)), y1 + 1, work.Height);
        return new(x1, y1, x2 - x1, y2 - y1);
    }
}
