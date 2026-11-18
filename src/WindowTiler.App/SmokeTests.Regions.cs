using System.Text.Json;
using System.Windows.Interop;

namespace WindowTiler.App;

internal static partial class SmokeTests
{
    private static async Task VerifyRegionsAsync(MainWindow window, List<WindowItem> fixtures, string output)
    {
        var checks = new List<string>();
        var primary = window.Displays.First();
        var originalConfig = JsonSerializer.Deserialize<AppSettings>("{}")!;
        if (originalConfig.Regions.Count != 0 || new DisplayLayout().UseRegion || new DisplayLayout().Clone().UseRegion)
            throw new Exception("Old settings no longer default to full-screen");
        window.GroupModeBox.IsChecked = false; window.DisplayCombo.SelectedItem = primary;
        var source = new TilingRegion { Name = "中间工作区", X = 96, Y = 80, Width = primary.Work.Width - 192, Height = primary.Work.Height - 160 };
        var second = new TilingRegion { Name = "左侧工作区", X = 24, Y = 32, Width = primary.Work.Width / 2, Height = primary.Work.Height - 64 };
        window.StoreRegion(source); window.StoreRegion(second);
        window.RegionCombo.SelectedItem = source;
        if (window.Settings.Regions.Count != 2 || !window.Settings.SingleDisplayLayout!.UseRegion || window.Settings.SingleDisplayLayout.RegionId != source.Id)
            throw new Exception("Region CRUD did not update selection");
        var serialized = JsonSerializer.Serialize(window.Settings);
        File.WriteAllText(Path.Combine(output, "region-settings-roundtrip.json"), serialized);
        var restored = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(output, "region-settings-roundtrip.json")))!;
        if (restored.Regions.Count != 2 || restored.SingleDisplayLayout?.RegionId != source.Id ||
            restored.Regions[0].Resolve(primary.Work) != source.Resolve(primary.Work) || !restored.SingleDisplayLayout.Clone().UseRegion)
            throw new Exception("Region settings round-trip failed");
        checks.Add("PASS: old settings default to full-screen; multiple named schemes and active mode/selection survive JSON and layout cloning");

        var editor = new RegionEditor(primary, source) { Owner = window };
        editor.Show(); await Task.Delay(120);
        editor.SelectRectangle(new(800, 600), new(100, 80));
        if (!editor.TryCreateResult() || editor.Result!.Resolve(primary.Work) != new PixelRect(primary.Work.X + 100, primary.Work.Y + 80, 700, 520))
            throw new Exception("Reverse editor drag does not match pixel coordinates");
        editor.Coordinates[2].Text = "invalid";
        if (editor.TryCreateResult()) throw new Exception("Region editor accepted non-numeric dimensions");
        editor.Coordinates[2].Text = int.MaxValue.ToString();
        if (editor.TryCreateResult()) throw new Exception("Region editor accepted out-of-bounds dimensions");
        editor.SetRectangle(new(source.X, source.Y, source.Width, source.Height));
        Screenshot(editor, Path.Combine(output, "region-editor.png"));
        ThemeService.Apply(AppTheme.Dark); await Task.Delay(80); Screenshot(editor, Path.Combine(output, "region-editor-dark.png"));
        editor.Close(); ThemeService.Apply(AppTheme.Light);
        if (source.X != 96 || source.Width != primary.Work.Width - 192) throw new Exception("Cancel edited the saved region in place");
        var clamped = RegionSelectionOverlay.RectangleFromPoints(new(-500, -20), new(primary.Work.Width + 400, primary.Work.Height + 300), primary.Work);
        if (clamped != new PixelRect(0, 0, primary.Work.Width, primary.Work.Height)) throw new Exception("Screen drag escaped the monitor");
        foreach (var display in window.Displays)
        {
            var overlay = new RegionSelectionOverlay(display) { Owner = window };
            overlay.Show(); await Task.Delay(150);
            Native.GetWindowRect(new WindowInteropHelper(overlay).Handle, out var bounds);
            overlay.Close();
            if (bounds.Pixels != display.Work) throw new Exception("Screen selection overlay is not on the selected monitor work area");
        }
        checks.Add("PASS: reverse selection, off-screen clamping, numeric validation, cancel isolation, light/dark editor and selection overlay bounds on physical monitors");

        async Task CheckPlacementAsync(bool aspect, bool custom)
        {
            foreach (var item in fixtures)
            {
                Native.SendMessageTimeout(item.Handle, AspectConstraintMessage, aspect ? 1u : 0u, 0, 2, 200, out _);
                item.KeepAspectRatio = aspect;
            }
            window.LayoutCombo.SelectedValue = custom ? LayoutKind.Custom : LayoutKind.Grid;
            var originals = fixtures.Select(x => { var p = Native.Placement.Create(); Native.GetWindowPlacement(x.Handle, ref p); return p; }).ToArray();
            window.ShowPanel(); await window.ApplyAsync();
            var ordered = window.Windows.Where(x => x.IsSelected).ToArray();
            if (ordered.Any(x => !x.Result.StartsWith("已")) || !Native.GroupIsInFront(ordered)) throw new Exception("Region tiling/front order failed");
            var layout = window.Settings.SingleDisplayLayout!;
            var work = source.Resolve(primary.Work);
            var template = window.Settings.Templates.FirstOrDefault(x => x.Id == layout.TemplateId);
            var zones = LayoutEngine.Arrange(work, ordered.Length, layout.Kind, layout.Gap, layout.MainRatio, template?.Root);
            var expected = aspect ? CompactLayout.Arrange(work, zones, ordered.Select((x, i) => AspectFit.Fit(zones[i], x.ClientAspectRatio, x.FrameWidth, x.FrameHeight)).ToArray(), layout.Gap) : zones;
            for (int i = 0; i < ordered.Length; i++)
                if (!CloseRect(Native.VisibleRect(ordered[i].Handle), expected[i])) throw new Exception("Region geometry mismatch");
            window.ShowPanel(); window.UpdateLayout(); await Task.Delay(80);
            double scale = Math.Min(window.Preview.ActualWidth / primary.Work.Width, window.Preview.ActualHeight / primary.Work.Height);
            double ox = (window.Preview.ActualWidth - primary.Work.Width * scale) / 2;
            double oy = (window.Preview.ActualHeight - primary.Work.Height * scale) / 2;
            var cells = window.Preview.Children.OfType<Border>().Where(x => x.Tag is WindowItem).ToArray();
            if (cells.Length != ordered.Length) throw new Exception("Region preview lost draggable cells");
            for (int i = 0; i < cells.Length; i++)
                if (Math.Abs(Canvas.GetLeft(cells[i]) - ox - (expected[i].X - primary.Work.X) * scale) > 1 ||
                    Math.Abs(Canvas.GetTop(cells[i]) - oy - (expected[i].Y - primary.Work.Y) * scale) > 1)
                    throw new Exception("Region preview does not match actual placement");
            if (!custom) Screenshot(window, Path.Combine(output, aspect ? "region-aspect-preview.png" : "region-preview.png"));
            await window.UndoAsync();
            if (window.Manager.CanUndo) throw new Exception("Region undo incomplete");
            for (int i = 0; i < fixtures.Count; i++)
            {
                var p = Native.Placement.Create(); Native.GetWindowPlacement(fixtures[i].Handle, ref p);
                if (p.ShowCmd != originals[i].ShowCmd || !p.NormalPosition.Equals(originals[i].NormalPosition)) throw new Exception("Region undo lost original placement");
            }
        }
        try
        {
            await CheckPlacementAsync(false, false);
            await CheckPlacementAsync(true, false);
            var custom = new LayoutTemplate { Name = "区域内四格", Root = new() { Axis = "X", Ratio = .5,
                First = new() { Axis = "Y", Ratio = .5, First = new(), Second = new() }, Second = new() { Axis = "Y", Ratio = .5, First = new(), Second = new() } } };
            window.Settings.Templates.Add(custom); window.TemplateCombo.Items.Refresh(); window.TemplateCombo.SelectedItem = custom;
            await CheckPlacementAsync(false, true);
            checks.Add("PASS: normal/aspect/custom layouts use region geometry; preview cells match actual bounds and original placements undo correctly");

            window.LayoutCombo.SelectedValue = LayoutKind.Grid;
            var previous = fixtures.Select(x => Native.VisibleRect(x.Handle)).ToArray();
            source.Width = primary.Work.Width + 1;
            await window.ApplyAsync();
            if (!window.StatusText.Text.Contains("超出") || fixtures.Where((x, i) => Native.VisibleRect(x.Handle) != previous[i]).Any()) throw new Exception("Invalid region moved windows");
            source.Width = primary.Work.Width - 192;
            window.Settings.SingleDisplayLayout!.RegionId = "missing";
            await window.ApplyAsync();
            if (!window.StatusText.Text.Contains("方案") || fixtures.Where((x, i) => Native.VisibleRect(x.Handle) != previous[i]).Any()) throw new Exception("Missing region fell back to full-screen");
            window.Settings.SingleDisplayLayout.RegionId = source.Id;
            checks.Add("PASS: out-of-bounds and missing schemes block tiling before moving any target");

            if (window.Displays.Count > 1)
            {
                var secondary = window.Displays[1];
                window.DisplayCombo.SelectedItem = secondary;
                if (window.Settings.SingleDisplayLayout.RegionId != source.Id || window.TilingArea(secondary, window.Settings.SingleDisplayLayout) != source.Resolve(secondary.Work))
                    throw new Exception("Switching monitors lost region choice or relative origin");
                window.DisplayCombo.SelectedItem = primary; window.GroupModeBox.IsChecked = true;
                window.DisplayCombo.SelectedItem = secondary; window.StoreRegion(second);
                for (int i = 0; i < fixtures.Count; i++) fixtures[i].DisplayKey = i % 2 == 0 ? primary.Key : secondary.Key;
                window.LayoutCombo.SelectedValue = LayoutKind.Grid;
                await window.ApplyAsync();
                foreach (var group in window.Windows.Where(x => x.IsSelected).GroupBy(x => x.DisplayKey))
                {
                    var display = window.Displays.First(x => x.Key == group.Key);
                    var layout = window.Settings.Displays[group.Key];
                    var zones = LayoutEngine.Arrange(window.TilingArea(display, layout), group.Count(), layout.Kind, layout.Gap, layout.MainRatio);
                    if (group.Where((x, i) => !CloseRect(Native.VisibleRect(x.Handle), zones[i])).Any()) throw new Exception("Per-monitor regions not applied independently");
                }
                if (!Native.GroupIsInFront(window.Windows.Where(x => x.IsSelected).ToArray())) throw new Exception("Region grouping changed front order");
                await window.UndoAsync(); if (window.Manager.CanUndo) throw new Exception("Grouped region undo failed");
                checks.Add("PASS: display switching reuses relative region offsets; grouped monitors use independent region schemes and retain front order/undo");
            }
            window.RemoveRegion(source); window.RemoveRegion(second);
            if (window.Settings.Displays.Values.Any(x => x.UseRegion) || window.Settings.SingleDisplayLayout!.UseRegion)
                throw new Exception("Removing a scheme left referencing layouts active");
            window.GroupModeBox.IsChecked = false; window.DisplayCombo.SelectedItem = primary;
            if (window.TilingArea(primary, window.Settings.SingleDisplayLayout) != primary.Work || window.RegionModeCombo.SelectedIndex != 0)
                throw new Exception("Full-screen mode no longer uses original work area");
            checks.Add("PASS: deleting schemes resets all referring layouts to full-screen without affecting the tiling mode");
        }
        finally
        {
            foreach (var item in fixtures) { Native.SendMessageTimeout(item.Handle, AspectConstraintMessage, 0, 0, 2, 200, out _); item.KeepAspectRatio = false; }
            File.WriteAllText(Path.Combine(output, "region-regressions.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
