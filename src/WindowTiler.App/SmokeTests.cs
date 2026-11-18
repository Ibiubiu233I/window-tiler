using System.Diagnostics;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;

namespace WindowTiler.App;

internal static partial class SmokeTests
{
    internal static void StartFrontFixture()
    {
        new Window { Title = "铺窗测试窗口 独立进程", Width = 440, Height = 300, Left = 150, Top = 150,
            Content = new TextBlock { Text = "仅用于跨进程前置顺序测试", Margin = new Thickness(20) } }.Show();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        timer.Tick += (_, _) => Application.Current.Shutdown(); timer.Start();
    }
    private const int DelayNextPlacementMessage = 0x8049;
    private const int AspectConstraintMessage = 0x804A;
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct TestWindowPos
    {
        public nint Handle, After;
        public int X, Y, Width, Height;
        public uint Flags;
    }
    internal static void StartFixtures()
    {
        for (int i = 0; i < 4; i++)
        {
            var window = new Window { Title = $"铺窗测试窗口 {i + 1}", Width = 440, Height = 300, Left = 90 + 70 * i, Top = 100 + 70 * i,
                MinWidth = 120, MinHeight = 100, Content = new TextBlock { Text = $"Window Tiler test fixture {i + 1}\n仅用于验证窗口移动与恢复", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            window.Show();
            if (i == 1) window.WindowState = WindowState.Maximized;
            if (i == 2) window.WindowState = WindowState.Minimized;
            if (i == 3) { window.WindowState = WindowState.Maximized; window.WindowState = WindowState.Minimized; }
            int delayNextPlacement = 0;
            int aspectConstraint = 0;
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!.AddHook((nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message == DelayNextPlacementMessage) { delayNextPlacement = (int)wParam; handled = true; }
                if (message == AspectConstraintMessage) { aspectConstraint = (int)wParam; handled = true; }
                if (message == 0x0046 && delayNextPlacement > 0) // WM_WINDOWPOSCHANGING
                {
                    int delay = delayNextPlacement; delayNextPlacement = 0;
                    System.Threading.Thread.Sleep(delay);
                }
                if (message == 0x0046 && aspectConstraint > 0 && !Native.IsZoomed(hwnd) && !Native.IsIconic(hwnd))
                {
                    var position = System.Runtime.InteropServices.Marshal.PtrToStructure<TestWindowPos>(lParam);
                    if ((position.Flags & 1) == 0 && Native.GetWindowRect(hwnd, out var raw) && Native.GetClientRect(hwnd, out var client))
                    {
                        int frameWidth = raw.Right - raw.Left - client.Right;
                        int frameHeight = raw.Bottom - raw.Top - client.Bottom;
                        int contentWidth = aspectConstraint == 2 ? 640 : Math.Max(320, position.Width - frameWidth);
                        position.Width = contentWidth + frameWidth;
                        position.Height = (int)Math.Round(contentWidth * 9.0 / 16) + frameHeight;
                        System.Runtime.InteropServices.Marshal.StructureToPtr(position, lParam, false);
                    }
                }
                return 0;
            });
        }
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        timer.Tick += (_, _) => Application.Current.Shutdown(); timer.Start();
    }

    internal static async Task RunAsync(MainWindow window)
    {
        string output = Environment.GetEnvironmentVariable("WINDOW_TILER_TEST_OUTPUT") ?? Path.Combine(AppContext.BaseDirectory, "smoke-results");
        Directory.CreateDirectory(output);
        var results = new List<string>(); Process? child = null; int exitCode = 0;
        try
        {
            ThemeService.Apply(AppTheme.Light);
            child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--fixture") { UseShellExecute = false });
            if (child is null) throw new Exception("Cannot start test fixtures");
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(200); await window.RefreshAsync();
                if (window.Windows.Count(x => x.ProcessId == child.Id) == 4) break;
            }
            var fixtures = window.Windows.Where(x => x.ProcessId == child.Id).OrderBy(x => x.Title).ToList();
            if (fixtures.Count != 4) throw new Exception($"Expected four enumerated test fixtures, found {fixtures.Count}");
            results.Add("PASS: enumerate normal, maximized and minimized fixtures; exclude own UI");
            var screen = window.Displays.First();
            foreach (var item in fixtures) { item.DisplayKey = screen.Key; item.IsSelected = true; }
            await window.RefreshAsync();
            fixtures = window.Windows.Where(x => x.ProcessId == child.Id).OrderBy(x => x.Title).ToList();
            if (fixtures.Any(x => !x.IsSelected || x.DisplayKey != screen.Key)) throw new Exception("Refresh lost selection or monitor assignments");
            results.Add("PASS: preserve selected windows and target monitors across refresh");
            for (int i = 0; i < fixtures.Count; i++) window.Windows.Move(window.Windows.IndexOf(fixtures[i]), i);
            window.GapSlider.Value = 9; window.GapSlider.Value = 8;
            await VerifyPlacementRegressionsAsync(window, fixtures, output);
            results.Add("PASS: selected windows raised above an occluder without permanent topmost; switching the target display retains the layout and moves all selected windows");
            var before = fixtures.Select(x => { var p = Native.Placement.Create(); Native.GetWindowPlacement(x.Handle, ref p); return p; }).ToList();
            await window.ApplyAsync();
            if (fixtures.Any(x => x.Result != "已平铺")) throw new Exception(string.Join("; ", fixtures.Select(x => x.Title + ": " + x.Result)));
            if (fixtures.Any(x => Native.IsZoomed(x.Handle) || Native.IsIconic(x.Handle))) throw new Exception("Tiling retained a maximized/minimized state");
            results.Add("PASS: real SetWindowPos + DWM visible-frame alignment for normal, maximized, minimized and minimized-from-maximized windows");
            window.ShowPanel(); await Task.Delay(250); Screenshot(window, Path.Combine(output, "main-window.png"));
            foreach (var (selector, filename) in new[] { (window.LayoutCombo, "layout-dropdown.png"), (window.DisplayCombo, "display-dropdown.png") })
            {
                selector.IsDropDownOpen = true;
                await Task.Delay(200);
                var popup = selector.Template.FindName("PART_Popup", selector) as Popup;
                Screenshot(window, Path.Combine(output, filename), popup?.Child as FrameworkElement);
                selector.IsDropDownOpen = false;
            }
            var custom = new LayoutTemplate { Name = "专注工作", Root = new() { Axis = "X", Ratio = .6, First = new(), Second = new() { Axis = "Y", First = new(), Second = new() } } };
            await VerifyThemesAsync(window, custom, output);
            results.Add("PASS: default/system/light/dark modes, simulated system changes, live palette updates, settings save/cancel, and theme serialization");
            await window.UndoAsync();
            for (int i = 0; i < fixtures.Count; i++)
            {
                var now = Native.Placement.Create(); Native.GetWindowPlacement(fixtures[i].Handle, ref now);
                if (now.ShowCmd != before[i].ShowCmd || !now.NormalPosition.Equals(before[i].NormalPosition)) throw new Exception("Undo mismatch for " + fixtures[i].Title);
            }
            results.Add("PASS: restore original normal placement and maximized/minimized states");
            await VerifyPreviewSwapAsync(window, fixtures, output);
            results.Add("PASS: four-window preview drag exchanges only two slots, supports cancellation, preserves grouped assignments, and applies actual swapped positions");
            await VerifyRestoreRegressionsAsync(window, fixtures, output);
            results.Add("PASS: delayed restore and minimized-from-maximized become normal before tiling; restore timeout skips movement; undo preserves original state");
            await VerifyAspectConstraintsAsync(window, fixtures, output);
            screen = window.Displays.First();
            results.Add("PASS: content aspect with title bar, automatic constrained-window fit, fixed size, undersized-zone rollback, aspect preview and mixed tiling");
            await VerifyCompactAndFrontAsync(window, fixtures, output);
            results.Add("PASS: compact centered 2x2 aspect group at gaps 0/8, preview alignment, ordered front group across separate processes, and original topmost flags preserved");
            if (window.Displays.Count > 1)
            {
                var secondary = window.Displays[1];
                window.GroupModeBox.IsChecked = true;
                fixtures[1].DisplayKey = secondary.Key;
                window.LayoutCombo.SelectedValue = LayoutKind.Vertical;
                window.DisplayCombo.SelectedItem = secondary;
                window.LayoutCombo.SelectedValue = LayoutKind.Horizontal;
                await window.ApplyAsync();
                if (fixtures.Any(x => x.Result != "已平铺")) throw new Exception("Cross-monitor placement failed");
                if (!Native.GroupIsInFront(window.Windows.Where(x => x.IsSelected).ToArray())) throw new Exception("Display grouping changed the global front order");
                foreach (var group in window.Windows.Where(x => x.IsSelected).GroupBy(x => x.DisplayKey))
                {
                    var display = window.Displays.First(x => x.Key == group.Key);
                    var expected = LayoutEngine.Arrange(display.Work, group.Count(), display.Key == secondary.Key ? LayoutKind.Horizontal : LayoutKind.Vertical, 8);
                    if (group.Select((item, index) => CloseRect(Native.VisibleRect(item.Handle), expected[index])).Any(x => !x)) throw new Exception("Grouped layouts or target bounds differ from preview");
                }
                await window.UndoAsync();
                if (window.Manager.CanUndo) { await Task.Delay(150); await window.UndoAsync(); }
                if (window.Manager.CanUndo) throw new Exception("Grouped undo did not restore every fixture");
                window.GroupModeBox.IsChecked = false;
                window.DisplayCombo.SelectedItem = screen;
                window.LayoutCombo.SelectedValue = LayoutKind.Grid;
                results.Add("PASS: explicit cross-monitor grouping with independent layouts and verified undo on physical displays");
            }
            else results.Add("NOT RUN: physical mixed-DPI and multi-monitor tests (one connected monitor)");
            results.Add("NOT RUN: changing physical display scaling during a run; elevated third-party applications; clean-machine installer lifecycle");
            var editor = new LayoutEditor(custom) { Owner = window }; editor.Show(); await Task.Delay(150); Screenshot(editor, Path.Combine(output, "layout-editor.png")); editor.Close();
            results.Add("PASS: render custom split editor");
            var settings = new SettingsWindow(window.Settings, null) { Owner = window }; settings.Show(); await Task.Delay(150); Screenshot(settings, Path.Combine(output, "settings.png")); settings.Close();
            results.Add("PASS: render settings dialog");
            using (var hotkeys = new HotkeyService(window))
            {
                if (!hotkeys.Register("Ctrl+Alt+Shift+F10", "Ctrl+Alt+Shift+F11", "Ctrl+Alt+Shift+F12", out string error)) throw new Exception(error);
                if (hotkeys.Register("Ctrl+Alt+F9", "Ctrl+Alt+F9", "Ctrl+Alt+F8", out _)) throw new Exception("Duplicate shortcuts accepted");
                results.Add("PASS: register global hotkeys and reject duplicate shortcuts");
            }
            // Exercise non-resizable targets and a closed handle without touching user applications.
            Native.SetWindowPos(fixtures[0].Handle, 0, screen.Work.X + 50, screen.Work.Y + 50, 440, 300, 0x0014);
            var tiny = new PixelRect(screen.Work.X + 50, screen.Work.Y + 50, 60, 60);
            await window.Manager.ApplyAsync([(fixtures[0], tiny)]);
            if (fixtures[0].Result == "已平铺") throw new Exception("Minimum-size constraint reported as success");
            await window.Manager.UndoAsync(); results.Add("PASS: report target minimum-size constraint");
            await VerifyPartialFailureAsync(window, fixtures);
            results.Add("PASS: concurrent successful and rejected targets preserve independent results and original undo snapshots");
            await MeasurePerformanceAsync(window, fixtures, output);
            results.Add("PASS: timed four-window normal/maximized/aspect scenarios with geometry, front order and undo verification");
            await VerifyRegionsAsync(window, fixtures, output);
            results.Add("PASS: saved regions, editor selection/cancel/validation, full-screen default, region preview, normal/aspect/custom tiling, monitor grouping and undo");
            child.Kill(); await child.WaitForExitAsync();
            await window.Manager.ApplyAsync([(fixtures[0], screen.Work)]);
            if (!fixtures[0].Result.Contains("已关闭")) throw new Exception("Closed-window handling failed");
            results.Add("PASS: safely skip closed windows");
        }
        catch (Exception ex) { exitCode = 1; results.Add("FAIL: " + ex); }
        finally
        {
            if (child is not null) { if (!child.HasExited) child.Kill(); child.Dispose(); }
            File.WriteAllText(Path.Combine(output, "smoke-report.json"), JsonSerializer.Serialize(new { Success = exitCode == 0, Date = DateTimeOffset.Now, Displays = Native.Displays().Select(x => new { x.Key, x.Bounds, x.Work }), Results = results }, new JsonSerializerOptions { WriteIndented = true }));
            window.ExitRequested = true; Application.Current.Shutdown(exitCode);
        }
    }

    private static async Task VerifyPartialFailureAsync(MainWindow window, List<WindowItem> fixtures)
    {
        var items = new[] { fixtures[0], fixtures[2] };
        var originals = items.Select(x => { var p = Native.Placement.Create(); Native.GetWindowPlacement(x.Handle, ref p); return p; }).ToArray();
        var work = window.Displays.First().Work;
        var target = new PixelRect(work.X + 24, work.Y + 24, 640, 400);
        var rejected = new PixelRect(work.X + 800, work.Y + 24, 40, 40);
        var result = await window.Manager.ApplyAsync([(items[0], target), (items[1], rejected)]);
        var rolledBack = Native.Placement.Create(); Native.GetWindowPlacement(items[1].Handle, ref rolledBack);
        if (result != (1, 2) || !CloseRect(Native.VisibleRect(items[0].Handle), target) ||
            !items[1].Result.Contains("已恢复原位置") || rolledBack.ShowCmd != originals[1].ShowCmd ||
            !rolledBack.NormalPosition.Equals(originals[1].NormalPosition))
            throw new Exception("Concurrent partial failure did not preserve successful placement and rejected target rollback");
        await window.Manager.UndoAsync();
        if (window.Manager.CanUndo) throw new Exception("Concurrent partial failure undo incomplete");
        for (int i = 0; i < items.Length; i++)
        {
            var restored = Native.Placement.Create(); Native.GetWindowPlacement(items[i].Handle, ref restored);
            if (restored.ShowCmd != originals[i].ShowCmd || !restored.NormalPosition.Equals(originals[i].NormalPosition) ||
                (restored.Flags & 2) != (originals[i].Flags & 2))
                throw new Exception("Concurrent partial failure lost an original undo snapshot");
        }
    }

    private static async Task MeasurePerformanceAsync(MainWindow window, List<WindowItem> fixtures, string output)
    {
        var rows = new List<object>();
        window.GroupModeBox.IsChecked = false; window.DisplayCombo.SelectedItem = window.Displays.First();
        window.LayoutCombo.SelectedValue = LayoutKind.Grid; window.GapSlider.Value = 8;
        try
        {
            foreach (string scenario in new[] { "normal-four", "maximized-four", "aspect-four" })
            {
                var samples = new List<double>();
                for (int trial = 0; trial < 3; trial++)
                {
                    foreach (var item in fixtures)
                    {
                        Native.SendMessageTimeout(item.Handle, AspectConstraintMessage, 0, 0, 2, 200, out _);
                        item.KeepAspectRatio = false;
                        var normal = Native.Placement.Create(); Native.GetWindowPlacement(item.Handle, ref normal);
                        normal.ShowCmd = 1; normal.Flags = 4; Native.SetWindowPlacement(item.Handle, in normal);
                    }
                    await Task.Delay(400);
                    foreach (var item in fixtures)
                    {
                        Native.GetWindowRect(item.Handle, out var raw); Native.GetClientRect(item.Handle, out var client);
                        Native.SetWindowPos(item.Handle, 0, 100, 100, 640 + raw.Right - raw.Left - client.Right,
                            360 + raw.Bottom - raw.Top - client.Bottom, 0x0014);
                    }
                    await Task.Delay(200);
                    foreach (var item in fixtures)
                    {
                        if (scenario == "maximized-four") Native.ShowWindowAsync(item.Handle, 3);
                        if (scenario == "aspect-four")
                        {
                            Native.SendMessageTimeout(item.Handle, AspectConstraintMessage, 1, 0, 2, 200, out _);
                            item.KeepAspectRatio = true;
                        }
                    }
                    await Task.Delay(300);
                    var originals = fixtures.Select(x => { var p = Native.Placement.Create(); Native.GetWindowPlacement(x.Handle, ref p); return p; }).ToArray();
                    window.ShowPanel(); await Task.Delay(80);
                    var timer = Stopwatch.StartNew();
                    await window.ApplyAsync();
                    timer.Stop();
                    if (fixtures.Any(x => !x.Result.StartsWith("已")) || !Native.GroupIsInFront(window.Windows.Where(x => x.IsSelected).ToArray()))
                        throw new Exception("Performance run failed: " + string.Join(";", fixtures.Select(x => x.Result)));
                    var zones = LayoutEngine.Arrange(window.Displays.First().Work, fixtures.Count, LayoutKind.Grid, 8);
                    var expected = scenario == "aspect-four"
                        ? CompactLayout.Arrange(window.Displays.First().Work, zones, fixtures.Select((x, i) => AspectFit.Fit(zones[i], x.ClientAspectRatio, x.FrameWidth, x.FrameHeight)).ToArray(), 8)
                        : zones;
                    if (fixtures.Where((x, i) => !CloseRect(Native.VisibleRect(x.Handle), expected[i])).Any()) throw new Exception("Timed layout geometry mismatch");
                    samples.Add(Math.Round(timer.Elapsed.TotalMilliseconds, 1));
                    await window.UndoAsync();
                    for (int i = 0; i < fixtures.Count; i++)
                    {
                        var current = Native.Placement.Create(); Native.GetWindowPlacement(fixtures[i].Handle, ref current);
                        if (current.ShowCmd != originals[i].ShowCmd || !current.NormalPosition.Equals(originals[i].NormalPosition)) throw new Exception($"Timed layout undo mismatch: {scenario}, trial={trial}, item={i}, result={fixtures[i].Result}, canUndo={window.Manager.CanUndo}, before=({originals[i].ShowCmd}, {originals[i].Flags}, {originals[i].NormalPosition.Pixels}), after=({current.ShowCmd}, {current.Flags}, {current.NormalPosition.Pixels})");
                    }
                }
                rows.Add(new { Scenario = scenario, SamplesMs = samples, MedianMs = samples.OrderBy(x => x).ElementAt(1) });
            }
        }
        finally
        {
            foreach (var item in fixtures) Native.SendMessageTimeout(item.Handle, AspectConstraintMessage, 0, 0, 2, 200, out _);
            File.WriteAllText(Path.Combine(output, "performance.json"), JsonSerializer.Serialize(new { Date = DateTimeOffset.Now, Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static async Task VerifyPlacementRegressionsAsync(MainWindow window, List<WindowItem> fixtures, string output)
    {
        var primary = window.Displays.First();
        var originalPlacements = fixtures.Select(item => { var p = Native.Placement.Create(); Native.GetWindowPlacement(item.Handle, ref p); return p; }).ToList();
        var checks = new List<object>();
        var failures = new List<string>();
        var blocker = new Window { Title = "铺窗测试遮挡窗口", Width = 600, Height = 420, Left = 120, Top = 120,
            Content = new TextBlock { Text = "仅用于验证平铺窗口能否显示在前面", Margin = new Thickness(24) } };
        try
        {
            // Normal windows must also be raised; restoring a minimized/maximized window
            // can activate it incidentally and otherwise mask the missing Z-order update.
            foreach (var item in fixtures) Native.ShowWindowAsync(item.Handle, 9);
            await Task.Delay(350);
            blocker.Show(); window.ShowPanel(); await Task.Delay(100);
            await window.ApplyAsync(); await Task.Delay(100);
            var order = new List<nint>();
            Native.EnumWindows((handle, _) => { if (Native.IsWindowVisible(handle)) order.Add(handle); return true; }, 0);
            int blockerIndex = order.IndexOf(new WindowInteropHelper(blocker).Handle);
            int panelIndex = order.IndexOf(new WindowInteropHelper(window).Handle);
            bool raised = fixtures.All(item => order.IndexOf(item.Handle) >= 0 && order.IndexOf(item.Handle) < blockerIndex && order.IndexOf(item.Handle) < panelIndex);
            bool notPinned = fixtures.All(item => (Native.GetWindowLongPtr(item.Handle, -20).ToInt64() & 8) == 0);
            checks.Add(new { Case = "raise-above-occluder", Raised = raised, NoPermanentTopmost = notPinned,
                BlockerIndex = blockerIndex, PanelIndex = panelIndex, Foreground = Native.GetForegroundWindow().ToInt64(),
                Windows = fixtures.Select(x => new { x.Title, Handle = x.Handle.ToInt64(), Index = order.IndexOf(x.Handle), x.Result, Bounds = Native.VisibleRect(x.Handle) }).ToList() });
            if (!raised || !notPinned) failures.Add("平铺窗口没有全部前置，或被意外永久置顶");
            await window.UndoAsync();

            if (window.Displays.Count > 1)
            {
                var secondary = window.Displays[1];
                window.LayoutCombo.SelectedValue = LayoutKind.MainLeft;
                window.GapSlider.Value = 12; window.RatioSlider.Value = 63;
                window.DisplayCombo.SelectedItem = secondary;
                window.UpdateLayout(); await Task.Delay(80);
                bool retained = Equals(window.LayoutCombo.SelectedValue, LayoutKind.MainLeft) && window.GapSlider.Value == 12 && window.RatioSlider.Value == 63;
                bool realPreview = window.Preview.Children.Count == fixtures.Count && !window.PreviewCaption.Text.Contains("示例");
                await window.ApplyAsync();
                var expected = LayoutEngine.Arrange(secondary.Work, fixtures.Count, LayoutKind.MainLeft, 12, .63);
                var selected = window.Windows.Where(x => x.IsSelected).ToList();
                bool moved = selected.Select((item, i) => CloseRect(Native.VisibleRect(item.Handle), expected[i])).All(x => x);
                checks.Add(new { Case = "select-screen-2-and-apply", LayoutRetained = retained, RealPreview = realPreview, MovedAllToScreen2 = moved,
                    Actual = selected.Select(x => new { x.Title, x.DisplayKey, Bounds = Native.VisibleRect(x.Handle) }).ToList(), Expected = expected });
                if (!retained || !realPreview || !moved) failures.Add("切换屏幕 2 后布局、预览或实际位置不一致");
                await window.UndoAsync();
            }
        }
        finally
        {
            blocker.Close(); window.DisplayCombo.SelectedItem = primary;
            window.LayoutCombo.SelectedValue = LayoutKind.Grid; window.GapSlider.Value = 8; window.RatioSlider.Value = 60;
            foreach (var item in fixtures) item.DisplayKey = primary.Key;
            for (int i = 0; i < fixtures.Count; i++) { var p = originalPlacements[i]; Native.SetWindowPlacement(fixtures[i].Handle, in p); }
            await Task.Delay(200);
            File.WriteAllText(Path.Combine(output, "placement-regressions.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (failures.Count > 0) throw new Exception(string.Join("；", failures));
    }

    private static async Task VerifyCompactAndFrontAsync(MainWindow window, List<WindowItem> fixtures, string output)
    {
        var originals = fixtures.Select(x => { var p = Native.Placement.Create(); Native.GetWindowPlacement(x.Handle, ref p); return (Window: x, Placement: p); }).ToList();
        var display = window.Displays.Last();
        var checks = new List<object>();
        Process? secondProcess = null;
        var blocker = new Window { Title = "铺窗整组前置测试遮挡窗口", Width = 700, Height = 500, Left = 120, Top = 100 };
        try
        {
            foreach (var item in fixtures)
            {
                var normal = Native.Placement.Create(); Native.GetWindowPlacement(item.Handle, ref normal);
                normal.Flags = 4; normal.ShowCmd = 1;
                Native.SetWindowPlacement(item.Handle, in normal);
            }
            await Task.Delay(350);
            foreach (var item in fixtures)
            {
                Native.GetWindowRect(item.Handle, out var raw); Native.GetClientRect(item.Handle, out var client);
                Native.SetWindowPos(item.Handle, 0, 100, 100, 640 + raw.Right - raw.Left - client.Right,
                    360 + raw.Bottom - raw.Top - client.Bottom, 0x0014);
            }
            await Task.Delay(200);
            foreach (var item in fixtures)
            {
                Native.SendMessageTimeout(item.Handle, AspectConstraintMessage, 1, 0, 2, 200, out _);
                item.KeepAspectRatio = true;
            }
            window.DisplayCombo.SelectedItem = display; window.LayoutCombo.SelectedValue = LayoutKind.Grid;
            foreach (int gap in new[] { 8, 0 })
            {
                window.GapSlider.Value = gap;
                blocker.Show(); window.ShowPanel();
                await window.ApplyAsync();
                var selected = window.Windows.Where(x => x.IsSelected).ToArray();
                var r = selected.Select(x => Native.VisibleRect(x.Handle)).ToArray();
                bool tight = r[1].X - r[0].Right == gap && r[3].X - r[2].Right == gap &&
                    r[2].Y - r[0].Bottom == gap && r[3].Y - r[1].Bottom == gap;
                bool centered = Math.Abs(r.Min(x => x.X) - display.Work.X - (display.Work.Right - r.Max(x => x.Right))) <= 1 &&
                    Math.Abs(r.Min(x => x.Y) - display.Work.Y - (display.Work.Bottom - r.Max(x => x.Bottom))) <= 1;
                if (!tight || !centered || selected.Any(x => !x.Result.StartsWith("已")) || !Native.GroupIsInFront(selected) || selected.Any(x => Native.IsTopmost(x.Handle)))
                    throw new Exception("Compact aspect group or front order failed: gap=" + gap + "; " + string.Join(";", selected.Select(x => x.Result)));
                window.UpdateLayout();
                double scale = Math.Min(window.Preview.ActualWidth / display.Work.Width, window.Preview.ActualHeight / display.Work.Height);
                double offsetX = (window.Preview.ActualWidth - display.Work.Width * scale) / 2;
                double offsetY = (window.Preview.ActualHeight - display.Work.Height * scale) / 2;
                foreach (var zone in window.Preview.Children.OfType<Border>())
                {
                    var actual = Native.VisibleRect(((WindowItem)zone.Tag).Handle);
                    if (Math.Abs(Canvas.GetLeft(zone) - offsetX - (actual.X - display.Work.X) * scale) > 1 ||
                        Math.Abs(Canvas.GetTop(zone) - offsetY - (actual.Y - display.Work.Y) * scale) > 1 ||
                        Math.Abs(zone.Width - actual.Width * scale) > 1 || Math.Abs(zone.Height - actual.Height * scale) > 1)
                        throw new Exception("Compact preview differs from actual group");
                }
                checks.Add(new { Case = "compact-2x2", Gap = gap, Tight = tight, Centered = centered, OrderedInFront = true, Bounds = r });
                window.ShowPanel(); await Task.Delay(100);
                Screenshot(window, Path.Combine(output, "compact-group-gap-" + gap + ".png"));
                await window.UndoAsync();
                if (window.Manager.CanUndo) throw new Exception("Compact group undo failed: " + string.Join(";", fixtures.Select(x => x.Title + ": " + x.Result)));
            }
            foreach (var item in fixtures)
            {
                Native.SendMessageTimeout(item.Handle, AspectConstraintMessage, 0, 0, 2, 200, out _);
                item.KeepAspectRatio = false;
            }
            secondProcess = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--front-fixture") { UseShellExecute = false });
            if (secondProcess is null) throw new Exception("Cannot start second target process");
            WindowItem? extra = null;
            for (int i = 0; i < 25 && extra is null; i++)
            {
                await Task.Delay(100);
                extra = Native.Windows(window.Displays).FirstOrDefault(x => x.ProcessId == secondProcess.Id);
            }
            if (extra is null) throw new Exception("Second process fixture not found");
            var ordered = new[] { fixtures[0], extra, fixtures[1], fixtures[2] };
            var zones = LayoutEngine.Arrange(window.Displays.First().Work, ordered.Length, LayoutKind.Grid, 8);
            foreach (bool preserveTopmost in new[] { false, true })
            {
                if (preserveTopmost) Native.SetWindowPos(ordered[0].Handle, -1, 0, 0, 0, 0, 0x0013 | 0x4000);
                blocker.Show(); window.ShowPanel(); await Task.Delay(80);
                var result = await window.Manager.ApplyAsync(ordered.Select((x, i) => (x, zones[i])).ToArray(), panel: new WindowInteropHelper(window).Handle);
                var order = Native.VisibleZOrder();
                var indices = ordered.Select(x => order.IndexOf(x.Handle)).ToArray();
                if (result.Success != ordered.Length || !indices.SequenceEqual(indices.OrderBy(x => x)) ||
                    indices.Max() >= order.IndexOf(new WindowInteropHelper(blocker).Handle) ||
                    Native.IsTopmost(ordered[0].Handle) != preserveTopmost || ordered.Skip(1).Any(x => Native.IsTopmost(x.Handle)))
                    throw new Exception("Cross-process front order/topmost preservation failed: " + string.Join(";", ordered.Select(x => x.Result)));
                checks.Add(new { Case = "cross-process-ordered-front", PreserveTopmost = preserveTopmost, Indices = indices, Success = true });
                await window.Manager.UndoAsync();
            }
        }
        finally
        {
            blocker.Close();
            if (secondProcess is not null) { if (!secondProcess.HasExited) secondProcess.Kill(); secondProcess.Dispose(); }
            foreach (var original in originals)
            {
                Native.SendMessageTimeout(original.Window.Handle, AspectConstraintMessage, 0, 0, 2, 200, out _);
                Native.SetWindowPos(original.Window.Handle, -2, 0, 0, 0, 0, 0x0013 | 0x4000);
                original.Window.KeepAspectRatio = false;
                var placement = original.Placement; Native.SetWindowPlacement(original.Window.Handle, in placement);
            }
            window.GapSlider.Value = 8; window.DisplayCombo.SelectedItem = window.Displays.First();
            await Task.Delay(200);
            File.WriteAllText(Path.Combine(output, "compact-front-regressions.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static async Task VerifyAspectConstraintsAsync(MainWindow window, List<WindowItem> fixtures, string output)
    {
        var item = fixtures[0];
        var originals = fixtures.Select(x => { var p = Native.Placement.Create(); Native.GetWindowPlacement(x.Handle, ref p); return (x.Handle, Placement: p); }).ToList();
        var primary = window.Displays[0];
        var checks = new List<object>();
        void Constraint(int mode)
        {
            if (Native.SendMessageTimeout(item.Handle, AspectConstraintMessage, (nuint)mode, 0, 2, 200, out _) == 0)
                throw new Exception("Cannot configure dedicated aspect fixture");
        }
        async Task ResetAsync()
        {
            Constraint(0); item.KeepAspectRatio = false;
            Native.ShowWindowAsync(item.Handle, 9); await Task.Delay(200);
            Native.GetWindowRect(item.Handle, out var raw); Native.GetClientRect(item.Handle, out var client);
            Native.SetWindowPos(item.Handle, 0, primary.Work.X + 80, primary.Work.Y + 80,
                640 + raw.Right - raw.Left - client.Right, 360 + raw.Bottom - raw.Top - client.Bottom, 0x0014);
            await Task.Delay(200);
        }
        try
        {
            foreach (var test in new[] {
                (Name: "manual-content-ratio-with-titlebar", Mode: 0, Keep: true, Zone: new PixelRect(32, 32, 700, 260)),
                (Name: "automatic-ratio-width-driven", Mode: 1, Keep: false, Zone: new PixelRect(32, 32, 700, 260)),
                (Name: "automatic-ratio-in-tall-zone", Mode: 1, Keep: false, Zone: new PixelRect(32, 32, 400, 620)),
                (Name: "fixed-size-centered", Mode: 2, Keep: false, Zone: new PixelRect(32, 32, 1000, 700)),
                (Name: "minimum-size-rollback", Mode: 1, Keep: true, Zone: new PixelRect(32, 32, 200, 120)) })
            {
                await ResetAsync(); Constraint(test.Mode); item.KeepAspectRatio = test.Keep;
                var before = Native.Placement.Create(); Native.GetWindowPlacement(item.Handle, ref before);
                var result = await window.Manager.ApplyAsync([(item, test.Zone)]);
                var actual = Native.VisibleRect(item.Handle); Native.GetClientRect(item.Handle, out var client);
                bool failed = test.Name == "minimum-size-rollback";
                if (failed)
                {
                    var restored = Native.Placement.Create(); Native.GetWindowPlacement(item.Handle, ref restored);
                    if (result.Success != 0 || !item.Result.Contains("已恢复原位置") || !restored.NormalPosition.Equals(before.NormalPosition))
                        throw new Exception("Undersized aspect zone was accepted or did not roll back: " + item.Result);
                }
                else if (result.Success != 1 || !item.KeepAspectRatio || Native.IsZoomed(item.Handle) ||
                    actual.X < test.Zone.X || actual.Y < test.Zone.Y || actual.Right > test.Zone.Right || actual.Bottom > test.Zone.Bottom ||
                    Math.Abs(client.Right - client.Bottom * 16.0 / 9) > 4 ||
                    Math.Abs(actual.X - test.Zone.X - (test.Zone.Right - actual.Right)) > 2 ||
                    Math.Abs(actual.Y - test.Zone.Y - (test.Zone.Bottom - actual.Bottom)) > 2)
                    throw new Exception(test.Name + " failed: " + item.Result + "; " + actual);
                checks.Add(new { test.Name, result.Success, item.Result, Zone = test.Zone, Actual = actual, ClientWidth = client.Right, ClientHeight = client.Bottom });
                await window.Manager.UndoAsync();
                if (window.Manager.CanUndo) throw new Exception("Aspect undo failed");
            }
            await ResetAsync();
            Native.ShowWindowAsync(item.Handle, 3); await Task.Delay(300);
            Constraint(1); item.KeepAspectRatio = true;
            if (item.ClientAspectRatio != 0) throw new Exception("Maximized dimensions must not be captured as the normal content ratio");
            var maxResult = await window.Manager.ApplyAsync([(item, new PixelRect(32, 32, 700, 260))]);
            Native.GetClientRect(item.Handle, out var maxClient);
            if (maxResult.Success != 1 || Native.IsZoomed(item.Handle) || Math.Abs(maxClient.Right - maxClient.Bottom * 16.0 / 9) > 4)
                throw new Exception("Maximized aspect window failed to restore and fit: " + item.Result);
            await window.Manager.UndoAsync();
            if (!Native.IsZoomed(item.Handle)) throw new Exception("Maximized aspect undo did not restore state");
            checks.Add(new { Name = "maximized-aspect-restore-and-undo", Success = true });

            await ResetAsync(); Constraint(1); item.KeepAspectRatio = true;
            window.DisplayCombo.SelectedItem = window.Displays.Last();
            window.LayoutCombo.SelectedValue = LayoutKind.Grid;
            window.ShowPanel(); await Task.Delay(100); window.UpdateLayout();
            var previewZone = window.Preview.Children.OfType<Border>().Single(x => ReferenceEquals(x.Tag, item));
            if (previewZone.Child is not Border) throw new Exception("Aspect preview is missing");
            await window.ApplyAsync();
            var display = window.Displays.Last();
            var zones = LayoutEngine.Arrange(display.Work, fixtures.Count, LayoutKind.Grid, 8);
            var selected = window.Windows.Where(x => x.IsSelected).ToList();
            var fittedSizes = selected.Select((entry, i) => entry.KeepAspectRatio ? AspectFit.Fit(zones[i], entry.ClientAspectRatio, entry.FrameWidth, entry.FrameHeight) : zones[i]).ToArray();
            var packed = CompactLayout.Arrange(display.Work, zones, fittedSizes, 8);
            foreach (var (entry, index) in selected.Select((x, i) => (x, i)))
            {
                var expected = packed[index];
                if (!CloseRect(Native.VisibleRect(entry.Handle), expected)) throw new Exception("Mixed aspect/fill placement differs from preview");
            }
            window.ShowPanel(); await Task.Delay(100); Screenshot(window, Path.Combine(output, "aspect-fit-preview.png"));
            double ratioBeforeRefresh = item.ClientAspectRatio;
            await window.RefreshAsync();
            var refreshed = window.Windows.Single(x => x.Handle == item.Handle);
            if (!refreshed.KeepAspectRatio || refreshed.ClientAspectRatio != ratioBeforeRefresh) throw new Exception("Refresh lost aspect preference");
            fixtures.Clear(); fixtures.AddRange(window.Windows.OrderBy(x => x.Title));
            await window.UndoAsync();
            if (window.Manager.CanUndo) throw new Exception("Mixed aspect/fill undo failed");
            checks.Add(new { Name = "mixed-fit-preview-target-monitor-refresh-and-undo", Success = true });
        }
        finally
        {
            Constraint(0);
            foreach (var entry in window.Windows) entry.KeepAspectRatio = false;
            window.DisplayCombo.SelectedItem = window.Displays.First(x => x.Key == primary.Key);
            for (int i = 0; i < originals.Count; i++) { var p = originals[i].Placement; Native.SetWindowPlacement(originals[i].Handle, in p); }
            await Task.Delay(200);
            File.WriteAllText(Path.Combine(output, "aspect-regressions.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static async Task VerifyPreviewSwapAsync(MainWindow window, List<WindowItem> fixtures, string output)
    {
        var originalOrder = window.Windows.ToList();
        var checks = new List<string>();
        var primary = window.Displays.First();
        static Point Center(Border zone) => new(Canvas.GetLeft(zone) + zone.Width / 2, Canvas.GetTop(zone) + zone.Height / 2);
        try
        {
            window.ShowPanel(); window.DisplayCombo.SelectedItem = primary;
            window.LayoutCombo.SelectedValue = LayoutKind.Grid; window.UpdateLayout();
            var zones = window.Preview.Children.OfType<Border>().ToList();
            if (zones.Count != 4) throw new Exception("Drag test requires four assigned preview zones");
            var first = (WindowItem)zones[0].Tag; var second = (WindowItem)zones[1].Tag;
            var beforeBounds = fixtures.Select(x => Native.VisibleRect(x.Handle)).ToList();
            if (!window.BeginPreviewDrag(Center(zones[0]))) throw new Exception("Preview rejected drag start");
            window.UpdatePreviewDrag(Center(zones[1]));
            if (zones[0].Opacity >= 1 || zones[1].BorderThickness.Left != 3) throw new Exception("Drag feedback missing");
            Screenshot(window, Path.Combine(output, "preview-drag.png"));
            if (!window.CompletePreviewDrag(Center(zones[1]))) throw new Exception("Drop did not exchange windows");
            var expectedOrder = originalOrder.ToList();
            expectedOrder[originalOrder.IndexOf(first)] = second; expectedOrder[originalOrder.IndexOf(second)] = first;
            if (!window.Windows.SequenceEqual(expectedOrder)) throw new Exception("Drop shifted windows instead of swapping exactly two slots");
            if (fixtures.Where((x, i) => Native.VisibleRect(x.Handle) != beforeBounds[i]).Any()) throw new Exception("Preview drag moved real windows before Apply");
            await Task.Delay(100);
            if (!window.WindowGrid.Items.Cast<WindowItem>().SequenceEqual(expectedOrder)) throw new Exception("Window list did not follow preview exchange");
            Screenshot(window, Path.Combine(output, "preview-swapped.png"));
            checks.Add("PASS: four equal grid cells, hover feedback, exact 1/2 exchange; other slots and actual windows unchanged before Apply");
            zones = window.Preview.Children.OfType<Border>().ToList();
            window.BeginPreviewDrag(Center(zones[0])); window.UpdatePreviewDrag(Center(zones[1])); window.CancelPreviewDrag();
            if (window.CompletePreviewDrag(Center(zones[1])) || !window.Windows.SequenceEqual(expectedOrder)) throw new Exception("Cancelled drag changed order");
            window.BeginPreviewDrag(Center(zones[0]));
            if (window.CompletePreviewDrag(new Point(-20, -20)) || !window.Windows.SequenceEqual(expectedOrder)) throw new Exception("Outside drop changed order");
            window.BeginPreviewDrag(Center(zones[0]));
            if (window.CompletePreviewDrag(Center(zones[0]))) throw new Exception("Click without drag changed order");
            checks.Add("PASS: cancel, outside drop and click retain original assignments");
            await window.ApplyAsync();
            var expectedBounds = LayoutEngine.Arrange(primary.Work, 4, LayoutKind.Grid, 8);
            if (window.Windows.Where((x, i) => !CloseRect(Native.VisibleRect(x.Handle), expectedBounds[i])).Any()) throw new Exception("Applied window bounds do not match swapped preview");
            await window.UndoAsync();
            if (window.Manager.CanUndo) throw new Exception("Swap apply undo failed");
            checks.Add("PASS: actual four-window tiling follows swapped preview; undo succeeds");
            if (window.Displays.Count > 1)
            {
                window.GroupModeBox.IsChecked = true;
                var otherScreenWindow = window.Windows[1]; otherScreenWindow.DisplayKey = window.Displays[1].Key;
                var uncheckedWindow = window.Windows[2]; uncheckedWindow.IsSelected = false;
                var groupedOrder = window.Windows.ToList();
                zones = window.Preview.Children.OfType<Border>().ToList();
                if (zones.Count != 2) throw new Exception("Grouped preview contains unchecked or other-screen windows");
                window.BeginPreviewDrag(Center(zones[0]));
                if (!window.CompletePreviewDrag(Center(zones[1])) || window.Windows[1] != otherScreenWindow || window.Windows[2] != uncheckedWindow ||
                    otherScreenWindow.DisplayKey != window.Displays[1].Key || window.Windows[0] != groupedOrder[3] || window.Windows[3] != groupedOrder[0])
                    throw new Exception("Grouped swap disturbed unrelated windows");
                checks.Add("PASS: grouped swap preserves unchecked rows, other-screen rows and target monitors");
            }
        }
        finally
        {
            window.CancelPreviewDrag();
            foreach (var item in fixtures) item.IsSelected = true;
            window.GroupModeBox.IsChecked = false; window.DisplayCombo.SelectedItem = primary;
            for (int i = 0; i < originalOrder.Count; i++) window.Windows.Move(window.Windows.IndexOf(originalOrder[i]), i);
            // Trigger the same preview refresh used when rows change.
            window.GapSlider.Value = 9; window.GapSlider.Value = 8;
            File.WriteAllText(Path.Combine(output, "preview-swap-report.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static async Task VerifyRestoreRegressionsAsync(MainWindow window, List<WindowItem> fixtures, string output)
    {
        var checks = new List<object>();
        var target = new PixelRect(window.Displays[0].Work.X + 32, window.Displays[0].Work.Y + 32, 720, 480);
        foreach (var (item, delay) in Enumerable.Range(0, 3).SelectMany(_ => new[] { (fixtures[1], 750), (fixtures[3], 0), (fixtures[1], 3500) }))
        {
            var before = Native.Placement.Create(); Native.GetWindowPlacement(item.Handle, ref before);
            if (delay > 0 && !Native.IsZoomed(item.Handle)) throw new Exception("Delayed restore test window is not maximized");
            if (delay == 0 && !Native.IsIconic(item.Handle)) throw new Exception("Minimized-from-maximized fixture lost minimized state");
            if (delay > 0) Native.SendMessageTimeout(item.Handle, DelayNextPlacementMessage, (nuint)delay, 0, 2, 200, out _);
            var result = await window.Manager.ApplyAsync([(item, target)]);
            string outcome = item.Result;
            if (delay < 2500)
            {
                if (result.Success != 1 || Native.IsZoomed(item.Handle) || Native.IsIconic(item.Handle) || !CloseRect(Native.VisibleRect(item.Handle), target))
                    throw new Exception($"Normal restore before tiling failed (delay {delay}): {outcome}");
            }
            else
            {
                if (result.Success != 0 || !outcome.Contains("超时")) throw new Exception("Restore timeout was not reported");
                await Task.Delay(1300);
                if (CloseRect(Native.VisibleRect(item.Handle), target)) throw new Exception("Timed-out restore still moved the window to the target");
            }
            if (delay == 750) Native.SendMessageTimeout(item.Handle, DelayNextPlacementMessage, 750, 0, 2, 200, out _);
            await window.Manager.UndoAsync();
            var after = Native.Placement.Create(); Native.GetWindowPlacement(item.Handle, ref after);
            if (window.Manager.CanUndo || before.ShowCmd != after.ShowCmd || !before.NormalPosition.Equals(after.NormalPosition) || (before.Flags & 2) != (after.Flags & 2))
                throw new Exception($"Restore regression undo lost original placement/state: delay={delay}, canUndo={window.Manager.CanUndo}, result={item.Result}, before=({before.ShowCmd}, {before.Flags}, {before.NormalPosition.Pixels}), after=({after.ShowCmd}, {after.Flags}, {after.NormalPosition.Pixels})");
            checks.Add(new { item.Title, DelayMs = delay, result.Success, Outcome = outcome, UndoPreserved = true });
        }
        File.WriteAllText(Path.Combine(output, "restore-regressions.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool CloseRect(PixelRect actual, PixelRect expected) => Math.Abs(actual.X - expected.X) <= 4 && Math.Abs(actual.Y - expected.Y) <= 4 &&
        Math.Abs(actual.Width - expected.Width) <= 4 && Math.Abs(actual.Height - expected.Height) <= 4;

    private static async Task VerifyThemesAsync(MainWindow window, LayoutTemplate template, string output)
    {
        var originalReader = ThemeService.SystemThemeReader;
        var originalMode = ThemeService.Mode;
        try
        {
            if (new AppSettings().Theme != AppTheme.Light || JsonSerializer.Deserialize<AppSettings>("{}")!.Theme != AppTheme.Light)
                throw new Exception("New settings and settings without a theme must default to light");
            ThemeService.SystemThemeReader = () => false;
            ThemeService.Apply(AppTheme.System);
            var background = ThemeService.Brush("WindowBackground");
            var lightColor = background.Color;
            ThemeService.SystemThemeReader = () => true;
            ThemeService.RefreshSystemTheme();
            if (!ThemeService.IsDark || background.Color == lightColor || !ReferenceEquals(window.Background, background))
                throw new Exception("Existing window failed to update on system theme change");
            ThemeService.Apply(AppTheme.Light);
            ThemeService.RefreshSystemTheme();
            if (ThemeService.IsDark) throw new Exception("Fixed light mode changed with system");
            ThemeService.Apply(AppTheme.Dark);
            ThemeService.SystemThemeReader = () => false;
            ThemeService.RefreshSystemTheme();
            if (!ThemeService.IsDark) throw new Exception("Fixed dark mode changed with system");
            var stored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { Theme = AppTheme.Dark }));
            if (stored?.Theme != AppTheme.Dark) throw new Exception("Theme preference was not preserved in JSON");

            window.ShowPanel(); await Task.Delay(180);
            Screenshot(window, Path.Combine(output, "main-window-dark.png"));
            window.LayoutCombo.IsDropDownOpen = true; await Task.Delay(200);
            var popup = window.LayoutCombo.Template.FindName("PART_Popup", window.LayoutCombo) as Popup;
            Screenshot(window, Path.Combine(output, "layout-dropdown-dark.png"), popup?.Child as FrameworkElement);
            window.LayoutCombo.IsDropDownOpen = false;

            var editor = new LayoutEditor(template) { Owner = window };
            editor.Show(); await Task.Delay(120);
            // Switch while templates and code-created brushes are already in use.
            ThemeService.Apply(AppTheme.Light); ThemeService.Apply(AppTheme.Dark);
            await Task.Delay(120); Screenshot(editor, Path.Combine(output, "layout-editor-dark.png")); editor.Close();
            var settings = new SettingsWindow(new AppSettings { Theme = AppTheme.Dark }, null) { Owner = window };
            settings.Show(); settings.ThemeSelector.SelectedValue = AppTheme.Light;
            if (ThemeService.IsDark) throw new Exception("Settings theme preview failed");
            settings.ThemeSelector.SelectedValue = AppTheme.Dark;
            await Task.Delay(150); Screenshot(settings, Path.Combine(output, "settings-dark.png"));
            settings.ThemeSelector.IsDropDownOpen = true; await Task.Delay(200);
            var themePopup = settings.ThemeSelector.Template.FindName("PART_Popup", settings.ThemeSelector) as Popup;
            Screenshot(settings, Path.Combine(output, "theme-dropdown-dark.png"), themePopup?.Child as FrameworkElement);
            settings.ThemeSelector.IsDropDownOpen = false;
            settings.ThemeSelector.SelectedValue = AppTheme.Light;
            settings.Close();
            if (!ThemeService.IsDark || ThemeService.Mode != AppTheme.Dark) throw new Exception("Cancel did not restore original theme");

            var savedSettings = new AppSettings { Theme = AppTheme.Light };
            var saveDialog = new SettingsWindow(savedSettings, null) { Owner = window };
            saveDialog.Loaded += (_, _) =>
            {
                saveDialog.ThemeSelector.SelectedValue = AppTheme.Dark;
                ((Panel)saveDialog.Content).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            if (saveDialog.ShowDialog() != true || savedSettings.Theme != AppTheme.Dark || !ThemeService.IsDark)
                throw new Exception("Save did not commit the selected theme");
        }
        finally
        {
            ThemeService.SystemThemeReader = originalReader;
            ThemeService.Apply(originalMode);
        }
    }

    private static void Screenshot(Window window, string path, FrameworkElement? overlay = null)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right), (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        if (overlay is not null)
        {
            // Capture the actual client area: popup HWNDs and their shadows cannot be
            // faithfully composited from a VisualBrush without clipping effect bounds.
            var dpi = VisualTreeHelper.GetDpi(content);
            var origin = content.PointToScreen(new Point(-content.Margin.Left, -content.Margin.Top));
            using var capture = new System.Drawing.Bitmap((int)Math.Round(bitmap.PixelWidth * dpi.DpiScaleX), (int)Math.Round(bitmap.PixelHeight * dpi.DpiScaleY));
            using var graphics = System.Drawing.Graphics.FromImage(capture);
            graphics.CopyFromScreen((int)Math.Round(origin.X), (int)Math.Round(origin.Y), 0, 0, capture.Size);
            capture.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return;
        }
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            drawing.DrawRectangle(new VisualBrush(content), null, new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
