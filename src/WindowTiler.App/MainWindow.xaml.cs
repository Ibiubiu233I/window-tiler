using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Interop;

namespace WindowTiler.App;

public partial class MainWindow : Window
{
    public ObservableCollection<DisplayItem> Displays { get; } = [];
    internal ObservableCollection<WindowItem> Windows { get; } = [];
    internal readonly AppSettings Settings;
    internal readonly WindowManager Manager = new();
    internal bool ExitRequested;
    private bool loading = true, refreshing, synchronizingTargets;
    private readonly string? initialWarning;
    private string? currentDisplay;
    internal bool IsBusy => Manager.Busy;
    private DisplayItem? ActiveDisplay => DisplayCombo.SelectedItem as DisplayItem;
    private DisplayLayout? ActiveLayout => currentDisplay is not null ? LayoutFor(currentDisplay) : null;
    private DisplayLayout? LayoutFor(string displayKey) => Settings.GroupByDisplay ? Settings.Displays.GetValueOrDefault(displayKey) : Settings.SingleDisplayLayout;

    public MainWindow()
    {
        Settings = App.SmokeMode ? new AppSettings() : SettingsStore.Load(out initialWarning);
        ThemeService.Apply(Settings.Theme);
        ThemeService.Track(this);
        InitializeComponent();
        InitializePreviewDrag();
        DataContext = this;
        WindowGrid.ItemsSource = Windows;
        DisplayCombo.ItemsSource = Displays;
        GroupModeBox.IsChecked = Settings.GroupByDisplay;
        LayoutCombo.ItemsSource = new Dictionary<LayoutKind, string> {
            [LayoutKind.Grid] = "自动均分网格", [LayoutKind.Horizontal] = "横向排列", [LayoutKind.Vertical] = "纵向排列",
            [LayoutKind.MainLeft] = "主窗口在左", [LayoutKind.MainRight] = "主窗口在右", [LayoutKind.MainTop] = "主窗口在上",
            [LayoutKind.MainBottom] = "主窗口在下", [LayoutKind.Custom] = "自定义分区模板" };
        TemplateCombo.ItemsSource = Settings.Templates;
        RegionCombo.ItemsSource = Settings.Regions;
        CollectionViewSource.GetDefaultView(Windows).Filter = item => item is WindowItem w &&
            (string.IsNullOrWhiteSpace(SearchBox.Text) || w.Title.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) || w.ProcessName.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
        loading = false;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!App.SmokeMode)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var handle = Native.MonitorFromWindow(new WindowInteropHelper(this).Handle, 2);
            var info = new Native.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>(), Device = "" };
            if (Native.GetMonitorInfo(handle, ref info))
            {
                Width = Math.Max(MinWidth, Math.Min(Width, info.Work.Pixels.Width / dpi.DpiScaleX - 32));
                Height = Math.Max(MinHeight, Math.Min(Height, info.Work.Pixels.Height / dpi.DpiScaleY - 32));
                Left = info.Work.Left / dpi.DpiScaleX + (info.Work.Pixels.Width / dpi.DpiScaleX - Width) / 2;
                Top = info.Work.Top / dpi.DpiScaleY + (info.Work.Pixels.Height / dpi.DpiScaleY - Height) / 2;
            }
        }
        await RefreshAsync();
        if (initialWarning is not null) Status(initialWarning);
    }

    internal async Task RefreshAsync()
    {
        if (refreshing || Manager.Busy) return;
        refreshing = true;
        try
        {
            Status("正在读取窗口和显示器…");
            var displays = Native.Displays();
            var items = await Task.Run(() => Native.Windows(displays));
            if (App.SmokeMode) items = items.Where(x => x.Title.StartsWith("铺窗测试窗口 ")).ToList();
            loading = true;
            var previous = Windows.ToDictionary(x => x.Handle, x => (x.ProcessId, x.ProcessStarted, x.IsSelected, x.DisplayKey, x.Result,
                x.KeepAspectRatio, x.ClientAspectRatio, x.FrameWidth, x.FrameHeight, x.LastFitZone, x.LastFitSize));
            var order = Windows.Select((w, index) => (w.Handle, index)).ToDictionary(x => x.Handle, x => x.index);
            var target = currentDisplay;
            Displays.Clear(); foreach (var d in displays) Displays.Add(d);
            foreach (var display in displays) Settings.Displays.TryAdd(display.Key, new());
            foreach (var old in Windows) old.PropertyChanged -= WindowChanged;
            Windows.Clear();
            foreach (var item in items.OrderBy(x => order.GetValueOrDefault(x.Handle, int.MaxValue)))
            {
                if (previous.TryGetValue(item.Handle, out var old) && old.ProcessId == item.ProcessId && old.ProcessStarted == item.ProcessStarted)
                {
                    item.IsSelected = old.IsSelected;
                    item.KeepAspectRatio = old.KeepAspectRatio;
                    item.ClientAspectRatio = old.ClientAspectRatio;
                    item.FrameWidth = old.FrameWidth; item.FrameHeight = old.FrameHeight;
                    item.LastFitZone = old.LastFitZone; item.LastFitSize = old.LastFitSize;
                    // Keep a disconnected assignment, so Apply can report it instead of moving to the wrong screen.
                    item.DisplayKey = old.DisplayKey;
                    item.Result = displays.Any(x => x.Key == item.DisplayKey) ? old.Result : "目标显示器已断开，请重新分配";
                }
                item.PropertyChanged += WindowChanged; Windows.Add(item);
            }
            DisplayCombo.SelectedItem = target is null ? displays.FirstOrDefault() : displays.FirstOrDefault(x => x.Key == target);
            loading = false;
            LoadDisplay();
            Status($"已找到 {Windows.Count} 个窗口、{Displays.Count} 块显示器。勾选窗口后即可平铺。");
        }
        catch (Exception ex) { Status("刷新失败：" + ex.Message); }
        finally { loading = false; refreshing = false; UpdateSummary(); }
    }

    private void WindowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!loading && e.PropertyName == nameof(WindowItem.KeepAspectRatio) && sender is WindowItem item)
        {
            item.ClientAspectRatio = 0;
            item.LastFitZone = item.LastFitSize = null;
            if (item.KeepAspectRatio) Native.ReadAspect(item, true);
            UpdateSummary();
        }
        if (!loading && !synchronizingTargets && e.PropertyName is nameof(WindowItem.IsSelected) or nameof(WindowItem.DisplayKey)) UpdateSummary();
    }
    private void UpdateSummary()
    {
        SynchronizeSingleTarget();
        WindowCount.Text = $"{Windows.Count} 个可用窗口 · 已勾选 {Windows.Count(x => x.IsSelected)} 个";
        var visible = CollectionViewSource.GetDefaultView(Windows).Cast<WindowItem>().ToList();
        SelectAllBox.IsChecked = visible.Count > 0 && visible.All(x => x.IsSelected);
        RenderPreview();
    }
    private void SynchronizeSingleTarget()
    {
        if (loading || synchronizingTargets || Settings.GroupByDisplay || ActiveDisplay is not { } display) return;
        synchronizingTargets = true;
        try { foreach (var item in Windows.Where(x => x.IsSelected)) item.DisplayKey = display.Key; }
        finally { synchronizingTargets = false; }
    }
    private void LoadDisplay()
    {
        if (ActiveDisplay is not { } display)
        {
            // Keep the missing key so subsequent refreshes cannot silently pick another screen.
            RenderPreview(); PreviewCaption.Text = "请选择已连接的目标显示器。";
            ApplyButton.Content = "请选择目标屏幕";
            return;
        }
        currentDisplay = display.Key;
        Settings.SingleDisplayLayout ??= Settings.Displays[display.Key].Clone();
        var layout = ActiveLayout!;
        loading = true;
        LayoutCombo.SelectedValue = layout.Kind;
        GapSlider.Value = layout.Gap; RatioSlider.Value = layout.MainRatio * 100;
        TemplateCombo.SelectedItem = Settings.Templates.FirstOrDefault(x => x.Id == layout.TemplateId);
        LoadRegionOptions();
        loading = false;
        WindowTargetColumn.Visibility = Settings.GroupByDisplay ? Visibility.Visible : Visibility.Collapsed;
        AssignButton.Visibility = Settings.GroupByDisplay ? Visibility.Visible : Visibility.Collapsed;
        ScopeHint.Text = Settings.GroupByDisplay
            ? "跨屏分组已开启：各屏幕可单独配置布局和区域方案。"
            : "所有勾选窗口都会平铺到所选屏幕的指定范围。切换屏幕时保留布局和区域方案。";
        ApplyButton.Content = Settings.GroupByDisplay ? "按分组平铺" : $"平铺到屏幕 {display.Number}";
        UpdateLayoutPanels(); UpdateSummary();
    }
    private void UpdateLayoutPanels()
    {
        var kind = ActiveLayout?.Kind;
        RatioPanel.Visibility = kind is LayoutKind.MainLeft or LayoutKind.MainRight or LayoutKind.MainTop or LayoutKind.MainBottom ? Visibility.Visible : Visibility.Collapsed;
        TemplatePanel.Visibility = kind == LayoutKind.Custom ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SaveOptions()
    {
        if (loading || ActiveLayout is not { } layout) return;
        layout.Kind = LayoutCombo.SelectedValue is LayoutKind kind ? kind : LayoutKind.Grid;
        layout.Gap = (int)GapSlider.Value; layout.MainRatio = RatioSlider.Value / 100;
        layout.TemplateId = (TemplateCombo.SelectedItem as LayoutTemplate)?.Id;
        UpdateLayoutPanels(); RenderPreview(); SaveSettings();
    }
    internal void SaveSettings()
    {
        if (App.SmokeMode) return;
        try { SettingsStore.Save(Settings); } catch (Exception ex) { Status("设置保存失败：" + ex.Message); }
    }
    internal void Status(string text) => StatusText.Text = text;

    private void RenderPreview()
    {
        if (Preview is null || Preview.ActualWidth < 10 || Preview.ActualHeight < 10) return;
        CancelPreviewDrag();
        Preview.Children.Clear();
        if (ActiveDisplay is not { } display || ActiveLayout is not { } layout) return;
        var windows = Windows.Where(x => x.IsSelected && (!Settings.GroupByDisplay || x.DisplayKey == display.Key)).ToList();
        var template = Settings.Templates.FirstOrDefault(x => x.Id == layout.TemplateId);
        int count = windows.Count == 0 ? (layout.Kind == LayoutKind.Custom ? template?.Root.Leaves().Count() ?? 1 : 4) : windows.Count;
        try
        {
            var area = TilingArea(display, layout);
            var rectangles = LayoutEngine.Arrange(area, count, layout.Kind, layout.Gap, layout.MainRatio, template?.Root);
            var fittedRects = rectangles.Select((zone, i) =>
            {
                if (windows.Count <= i || !windows[i].KeepAspectRatio || windows[i].ClientAspectRatio <= 0) return zone;
                var item = windows[i];
                if (item.LastFitZone == zone && item.LastFitSize is { } size && size.Width <= zone.Width && size.Height <= zone.Height)
                    return new PixelRect(zone.X, zone.Y, size.Width, size.Height);
                return AspectFit.Fit(zone, item.ClientAspectRatio, item.FrameWidth, item.FrameHeight);
            }).ToArray();
            bool compact = layout.Kind != LayoutKind.Custom && windows.Count > 1 && windows.Any(x => x.KeepAspectRatio);
            if (compact) rectangles = CompactLayout.Arrange(area, rectangles, fittedRects, layout.Gap);
            double scale = Math.Min(Preview.ActualWidth / display.Work.Width, Preview.ActualHeight / display.Work.Height);
            double offsetX = (Preview.ActualWidth - display.Work.Width * scale) / 2, offsetY = (Preview.ActualHeight - display.Work.Height * scale) / 2;
            if (layout.UseRegion)
            {
                var screen = new Border { Width = display.Work.Width * scale, Height = display.Work.Height * scale,
                    Background = ThemeService.Brush("SurfaceSubtle"), BorderBrush = ThemeService.Brush("BorderSubtle"), BorderThickness = new Thickness(1), IsHitTestVisible = false };
                Canvas.SetLeft(screen, offsetX); Canvas.SetTop(screen, offsetY); Preview.Children.Add(screen);
                var regionBorder = new Border { Width = area.Width * scale, Height = area.Height * scale,
                    BorderBrush = ThemeService.Brush("Accent"), BorderThickness = new Thickness(2), IsHitTestVisible = false };
                Canvas.SetLeft(regionBorder, offsetX + (area.X - display.Work.X) * scale);
                Canvas.SetTop(regionBorder, offsetY + (area.Y - display.Work.Y) * scale); Preview.Children.Add(regionBorder);
            }
            for (int i = 0; i < rectangles.Count; i++)
            {
                var r = rectangles[i];
                var fitted = compact ? r : fittedRects[i];
                bool keepRatio = windows.Count > i && windows[i].KeepAspectRatio;
                var label = windows.Count == 0 ? $"分区 {i + 1}" : $"{i + 1}  {windows[i].Title}";
                var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                content.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                if (fitted.Height * scale > 55) content.Children.Add(new TextBlock { Text = $"{fitted.Width} × {fitted.Height}" + (keepRatio ? " · 保持比例" : ""), FontSize = 10, Foreground = ThemeService.Brush("TextSecondary"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
                var border = new Border { Width = Math.Max(1, r.Width * scale), Height = Math.Max(1, r.Height * scale), Background = ThemeService.Brush(i == 0 ? "ZonePrimary" : "ZoneSecondary"), BorderBrush = ThemeService.Brush("ZoneBorder"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(4), Child = content, ToolTip = windows.Count > i ? windows[i].Title : label };
                if (keepRatio)
                {
                    if (windows[i].ClientAspectRatio <= 0)
                        content.Children.Add(new TextBlock { Text = "恢复普通窗口后读取比例", FontSize = 10, TextAlignment = TextAlignment.Center });
                    border.Padding = new Thickness(0);
                    border.Child = null;
                    border.Child = new Border { Width = Math.Max(1, fitted.Width * scale - 2), Height = Math.Max(1, fitted.Height * scale - 2),
                        Background = border.Background, BorderBrush = ThemeService.Brush("ZoneBorder"), BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4), Padding = new Thickness(4), ClipToBounds = true, Child = content,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    border.Background = ThemeService.Brush("SurfaceSubtle");
                }
                if (windows.Count > i)
                {
                    border.Tag = windows[i];
                    border.Cursor = System.Windows.Input.Cursors.Hand;
                    border.ToolTip = windows[i].Title + "\n拖到另一窗口上交换位置，点击平铺后生效";
                }
                Canvas.SetLeft(border, offsetX + (r.X - display.Work.X) * scale); Canvas.SetTop(border, offsetY + (r.Y - display.Work.Y) * scale); Preview.Children.Add(border);
            }
            PreviewCaption.Text = windows.Count == 0 ? (Settings.GroupByDisplay ? "当前屏幕尚未分配窗口，以上为布局示例。" : "尚未勾选窗口，以上为布局示例。") : $"屏幕 {display.Number} · {windows.Count} 个窗口 · 可用区域 {display.Work.Width} × {display.Work.Height}" + (layout.Kind == LayoutKind.Custom && count < template!.Root.Leaves().Count() ? " · 未分配的分区保持空置" : "");
            if (layout.UseRegion) PreviewCaption.Text += $"\n划定区域 · {Settings.Regions.First(x => x.Id == layout.RegionId).Name} · {area.Width} × {area.Height}";
            if (windows.Any(x => x.KeepAspectRatio)) PreviewCaption.Text += compact
                ? "\n保持比例：窗口按设置间距靠拢，整组居中；间距设为 0 可紧贴。"
                : "\n保持比例：分区内居中留白，实际尺寸以程序允许值为准。";
        }
        catch (ArgumentException ex) { PreviewCaption.Text = ex.Message; }
    }

    internal async Task ApplyAsync()
    {
        if (Manager.Busy || refreshing) return;
        WindowGrid.CommitEdit(); WindowGrid.CommitEdit();
        SynchronizeSingleTarget();
        var selected = Windows.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) { Status("请先勾选要平铺的窗口。"); return; }
        var connected = Native.Displays();
        var plan = new List<(WindowItem Window, PixelRect Target)>();
        var groups = new List<TilingGroup>();
        try
        {
            if (!Settings.GroupByDisplay && ActiveDisplay is null) throw new ArgumentException("请选择目标显示器。");
            foreach (var group in selected.GroupBy(x => Settings.GroupByDisplay ? x.DisplayKey : ActiveDisplay!.Key))
            {
                var display = connected.FirstOrDefault(x => x.Key == group.Key) ?? throw new ArgumentException("有窗口的目标显示器已断开，请刷新并重新分配。");
                if (Displays.FirstOrDefault(x => x.Key == group.Key)?.Work != display.Work) throw new ArgumentException("显示器可用区域已变化，请刷新后检查预览再执行。");
                var layout = LayoutFor(group.Key) ?? throw new ArgumentException("请先选择平铺布局。");
                var template = Settings.Templates.FirstOrDefault(x => x.Id == layout.TemplateId);
                var area = TilingArea(display, layout);
                var rectangles = LayoutEngine.Arrange(area, group.Count(), layout.Kind, layout.Gap, layout.MainRatio, template?.Root);
                var groupPlan = group.Select((window, i) => (Window: window, Target: rectangles[i])).ToArray();
                groups.Add(new(area, layout.Gap, groupPlan, layout.Kind != LayoutKind.Custom));
                plan.AddRange(groupPlan);
            }
        }
        catch (ArgumentException ex) { Status(ex.Message); return; }
        ApplyButton.IsEnabled = UndoButton.IsEnabled = false;
        Status($"正在平铺 {plan.Count} 个窗口…");
        try
        {
            // Display grouping must not change the global front-to-back list order.
            plan = selected.Select(item => plan.Single(x => ReferenceEquals(x.Window, item))).ToList();
            var result = await Manager.ApplyAsync(plan, groups, new WindowInteropHelper(this).Handle);
            Status($"已平铺并前置 {result.Success}/{result.Total} 个窗口。" + (result.Success < result.Total ? "请查看列表中的执行结果。" : "可点击“恢复上次位置”撤销。"));
        }
        catch (Exception ex) { Status("平铺未完成：" + ex.Message); }
        finally { ApplyButton.IsEnabled = true; UndoButton.IsEnabled = Manager.CanUndo; RenderPreview(); }
    }
    internal async Task UndoAsync()
    {
        if (Manager.Busy) return;
        ApplyButton.IsEnabled = UndoButton.IsEnabled = false;
        try { Status(await Manager.UndoAsync()); }
        catch (Exception ex) { Status("恢复失败：" + ex.Message); }
        finally { ApplyButton.IsEnabled = true; UndoButton.IsEnabled = Manager.CanUndo; }
    }
    private async void Apply_Click(object sender, RoutedEventArgs e) => await ApplyAsync();
    private async void Undo_Click(object sender, RoutedEventArgs e) => await UndoAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (!loading) { CollectionViewSource.GetDefaultView(Windows).Refresh(); UpdateSummary(); } }
    private void Display_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        LoadDisplay();
        if (!Settings.GroupByDisplay && ActiveDisplay is { } display) Status($"目标已切换到屏幕 {display.Number}，将按当前预览平铺所有勾选窗口。");
    }
    private void GroupMode_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        WindowGrid.CommitEdit(); WindowGrid.CommitEdit();
        var currentLayout = ActiveLayout?.Clone();
        bool grouped = GroupModeBox.IsChecked == true;
        if (grouped && currentDisplay is not null && currentLayout is not null) Settings.Displays[currentDisplay] = currentLayout;
        else if (!grouped && currentLayout is not null) Settings.SingleDisplayLayout = currentLayout;
        Settings.GroupByDisplay = grouped;
        LoadDisplay(); SaveSettings();
    }
    private void Layout_Changed(object sender, SelectionChangedEventArgs e) => SaveOptions();
    private void Template_Changed(object sender, SelectionChangedEventArgs e) => SaveOptions();
    private void Options_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => SaveOptions();
    private void Preview_SizeChanged(object sender, SizeChangedEventArgs e) { if (!loading) RenderPreview(); }
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool value = SelectAllBox.IsChecked == true;
        loading = true;
        foreach (var item in CollectionViewSource.GetDefaultView(Windows).Cast<WindowItem>()) item.IsSelected = value;
        loading = false; UpdateSummary();
    }
    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveDisplay is not { } display) return;
        foreach (var item in Windows.Where(x => x.IsSelected)) item.DisplayKey = display.Key;
        Status($"已将勾选窗口分配到屏幕 {display.Number}，点击平铺后生效。");
    }
    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);
    private void MoveSelected(int delta)
    {
        if (WindowGrid.SelectedItem is not WindowItem item) return;
        int i = Windows.IndexOf(item), target = i + delta;
        if (target < 0 || target >= Windows.Count) return;
        Windows.Move(i, target); WindowGrid.SelectedItem = item; RenderPreview();
    }
    private void NewTemplate_Click(object sender, RoutedEventArgs e) => EditTemplate(null);
    private void EditTemplate_Click(object sender, RoutedEventArgs e) { if (TemplateCombo.SelectedItem is LayoutTemplate t) EditTemplate(t); else Status("请先选择要编辑的模板。"); }
    private void EditTemplate(LayoutTemplate? source)
    {
        var editor = new LayoutEditor(source) { Owner = this };
        if (editor.ShowDialog() != true) return;
        if (source is not null) Settings.Templates.Remove(source);
        Settings.Templates.Add(editor.Result!);
        TemplateCombo.Items.Refresh(); TemplateCombo.SelectedItem = editor.Result; SaveOptions();
    }
    private void CopyTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateCombo.SelectedItem is not LayoutTemplate source) return;
        var copy = new LayoutTemplate { Name = source.Name + " 副本", Root = source.Root.Clone() };
        Settings.Templates.Add(copy); TemplateCombo.Items.Refresh(); TemplateCombo.SelectedItem = copy; SaveOptions();
    }
    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateCombo.SelectedItem is not LayoutTemplate source) return;
        if (!Dialogs.Confirm(this, "删除模板", $"删除模板“{source.Name}”？使用它的屏幕将切换为自动网格。")) return;
        foreach (var layout in Settings.Displays.Values.Where(x => x.TemplateId == source.Id)) { layout.Kind = LayoutKind.Grid; layout.TemplateId = null; }
        if (Settings.SingleDisplayLayout is { } single && single.TemplateId == source.Id) { single.Kind = LayoutKind.Grid; single.TemplateId = null; }
        Settings.Templates.Remove(source); TemplateCombo.Items.Refresh(); LoadDisplay(); SaveSettings();
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        if (new SettingsWindow(Settings, app.Hotkeys) { Owner = this }.ShowDialog() == true)
        {
            ShortcutText.Text = "关闭窗口后驻留托盘 · 可在设置中修改快捷键";
            ShortcutText.Foreground = ThemeService.Brush("TextSecondary");
            SaveSettings();
        }
    }
    private void Identify_Click(object sender, RoutedEventArgs e)
    {
        foreach (var display in Native.Displays())
        {
            var badge = new Window { Title = "显示器编号", Width = 210, Height = 120, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Background = Brushes.Transparent, AllowsTransparency = true, Content = new Border { Background = (Brush)new BrushConverter().ConvertFrom("#EB305CE8")!, CornerRadius = new CornerRadius(16), Child = new TextBlock { Text = $"屏幕 {display.Number}", Foreground = Brushes.White, FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };
            badge.Show(); Native.SetWindowPos(new WindowInteropHelper(badge).Handle, -1, display.Work.X + display.Work.Width / 2 - 105, display.Work.Y + display.Work.Height / 2 - 60, 210, 120, 0x0010);
            _ = CloseBadgeAsync(badge);
        }
    }
    private static async Task CloseBadgeAsync(Window badge) { await Task.Delay(1800); badge.Close(); }
    internal void ShowPanel() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); Native.SetForegroundWindow(new WindowInteropHelper(this).Handle); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (ExitRequested) return;
        if (Settings.MinimizeToTray || Manager.Busy) { e.Cancel = true; Hide(); ((App)Application.Current).ExplainTrayOnce(); }
        else ((App)Application.Current).ExitApp();
    }
}
