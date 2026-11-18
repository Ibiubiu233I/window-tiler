using WindowTiler.Core;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, message); }
bool Overlap(PixelRect a, PixelRect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
var displays = new[] { new PixelRect(0, 0, 1920, 1040), new PixelRect(-2560, -350, 2560, 1400), new PixelRect(1920, -1080, 1080, 1880), new PixelRect(0, 48, 1537, 917) };
foreach (var display in displays)
foreach (var kind in Enum.GetValues<LayoutKind>().Where(x => x != LayoutKind.Custom))
foreach (int count in Enumerable.Range(1, 16))
foreach (int gap in new[] { 0, 1, 8 })
{
    var result = LayoutEngine.Arrange(display, count, kind, gap);
    Check(result.Count == count, "Must allocate exactly one rectangle per window");
    foreach (var r in result) Check(r.Width > 0 && r.Height > 0 && r.X >= display.X + gap && r.Y >= display.Y + gap && r.Right <= display.Right - gap && r.Bottom <= display.Bottom - gap, "Bounds and outer margins");
    for (int i = 0; i < count; i++) for (int j = i + 1; j < count; j++) Check(!Overlap(result[i], result[j]), "Windows overlap");
    if (gap == 0) Check(result.Sum(r => (long)r.Width * r.Height) == (long)display.Width * display.Height, "Unused cells in built-in layout");
}
var area = displays[0];
var horizontal = LayoutEngine.Arrange(area, 3, LayoutKind.Horizontal, 9);
Check(horizontal[1].X - horizontal[0].Right == 9, "Exact odd gap");
Check(horizontal[2].X - horizontal[1].Right == 9, "Exact second gap");
var mainRight = LayoutEngine.Arrange(area, 4, LayoutKind.MainRight, 0, .7);
Check(mainRight[0].Right == area.Right && mainRight[0].Width == 1344, "Main-right position and ratio");
var custom = new LayoutNode { Axis = "X", Ratio = .6, First = new(), Second = new() { Axis = "Y", Ratio = .4, First = new(), Second = new() } };
var customRects = LayoutEngine.Arrange(displays[1], 3, LayoutKind.Custom, 0, .6, custom);
Check(customRects[0].Width == 1536 && customRects[1].Height == 560, "Nested custom geometry");
Check(customRects.Sum(r => r.Width * r.Height) == 2560 * 1400, "Custom zones cover screen");
Check(LayoutEngine.Arrange(area, 1, LayoutKind.Custom, 0, .6, custom)[0].Width == 1152, "Unassigned custom zones remain empty");
Reject(() => LayoutEngine.Arrange(area, 4, LayoutKind.Custom, 0, .6, custom), "Custom capacity must reject excess windows");
Reject(() => LayoutEngine.Arrange(area, 1, LayoutKind.Custom), "Custom layout requires template");
Reject(() => LayoutEngine.Arrange(area, -1, LayoutKind.Grid), "Negative count");
Reject(() => LayoutEngine.Arrange(area, 1, LayoutKind.Grid, -1), "Negative gap");
Reject(() => LayoutEngine.Arrange(new(0, 0, 100, 100), 12, LayoutKind.Vertical, 8), "Too-small partitions");
Reject(() => LayoutEngine.Arrange(area, 2, LayoutKind.MainLeft, 0, double.NaN), "NaN ratio");
var invalid = new LayoutNode { First = new(), Ratio = .5 };
Reject(() => invalid.Validate(), "Malformed split tree");
var clone = custom.Clone(); clone.First!.Axis = "Y"; Check(custom.First!.Axis == "X", "Template editor must clone before editing");
Check(LayoutEngine.Arrange(area, 0, LayoutKind.Grid).Count == 0, "No selected windows");
foreach (var zone in displays.Concat(new[] { new PixelRect(-400, 0, 401, 803), new PixelRect(0, 0, 901, 251) }))
foreach (double ratio in new[] { 16.0 / 9, 4.0 / 3, 9.0 / 16, 1.0, 2.39 })
foreach (var frame in new[] { (Width: 0, Height: 0), (Width: 2, Height: 32), (Width: 4, Height: 64) })
{
    var fitted = AspectFit.Fit(zone, ratio, frame.Width, frame.Height);
    int clientWidth = fitted.Width - frame.Width, clientHeight = fitted.Height - frame.Height;
    Check(fitted.X >= zone.X && fitted.Y >= zone.Y && fitted.Right <= zone.Right && fitted.Bottom <= zone.Bottom, "Aspect fit contained in zone");
    Check(Math.Abs(clientWidth - clientHeight * ratio) <= Math.Max(1, ratio), "Content ratio excludes visible title bar and frame");
    Check(Math.Abs((fitted.X - zone.X) - (zone.Right - fitted.Right)) <= 1 && Math.Abs((fitted.Y - zone.Y) - (zone.Bottom - fitted.Bottom)) <= 1, "Aspect fit centered including negative coordinates");
    Check(fitted.Width == zone.Width || fitted.Height == zone.Height, "Fit maximizes one available dimension");
}
Reject(() => AspectFit.Fit(area, 0, 0, 0), "Zero aspect");
Reject(() => AspectFit.Fit(area, double.NaN, 0, 0), "NaN aspect");
Reject(() => AspectFit.Fit(area, double.PositiveInfinity, 0, 0), "Infinite aspect");
Reject(() => AspectFit.Fit(new(0, 0, 20, 20), 16.0 / 9, 0, 32), "No room for title bar");
foreach (var work in displays)
foreach (int gap in new[] { 0, 8 })
{
    var zones = LayoutEngine.Arrange(work, 4, LayoutKind.Grid, gap);
    // Explicit 2x2 topology, with equal fixed-size rectangles that fit every cell.
    zones = new[] { new PixelRect(0, 0, 500, 500), new PixelRect(500 + gap, 0, 500, 500),
        new PixelRect(0, 500 + gap, 500, 500), new PixelRect(500 + gap, 500 + gap, 500, 500) };
    var sizes = Enumerable.Repeat(new PixelRect(0, 0, 320, 212), 4).ToArray();
    var packed = CompactLayout.Arrange(work, zones, sizes, gap);
    Check(packed[1].X - packed[0].Right == gap && packed[3].X - packed[2].Right == gap, "No extra gap between columns");
    Check(packed[2].Y - packed[0].Bottom == gap && packed[3].Y - packed[1].Bottom == gap, "No extra gap between rows");
    Check(Math.Abs(packed[0].X - work.X - (work.Right - packed[3].Right)) <= 1 && Math.Abs(packed[0].Y - work.Y - (work.Bottom - packed[3].Bottom)) <= 1, "Entire compact group centered");
}
foreach (var kind in Enum.GetValues<LayoutKind>().Where(k => k != LayoutKind.Custom))
foreach (int count in Enumerable.Range(1, 16))
{
    var zones = LayoutEngine.Arrange(area, count, kind, 8);
    var sizes = zones.Select((r, i) => AspectFit.Fit(r, i % 2 == 0 ? 16.0 / 9 : 4.0 / 3, 2, 32)).ToArray();
    var packed = CompactLayout.Arrange(area, zones, sizes, 8);
    for (int i = 0; i < count; i++)
    {
        Check(packed[i].Width == sizes[i].Width && packed[i].Height == sizes[i].Height, "Compaction never stretches windows");
        Check(packed[i].X >= area.X + 8 && packed[i].Y >= area.Y + 8 && packed[i].Right <= area.Right - 8 && packed[i].Bottom <= area.Bottom - 8, "Compaction stays in work area");
        for (int j = i + 1; j < count; j++) Check(!Overlap(packed[i], packed[j]), "Mixed-size compaction must not overlap");
    }
}
foreach (var work in displays)
{
    var region = new TilingRegion { Name = "右侧区域", X = 90, Y = 60, Width = work.Width - 180, Height = work.Height - 120 };
    var resolved = region.Resolve(work);
    Check(resolved.X == work.X + 90 && resolved.Y == work.Y + 60 && resolved.Right == work.Right - 90 && resolved.Bottom == work.Bottom - 60, "Region offsets use monitor work origin, including negative coordinates and taskbar offsets");
    foreach (var kind in Enum.GetValues<LayoutKind>().Where(x => x != LayoutKind.Custom))
    {
        var zones = LayoutEngine.Arrange(resolved, 4, kind, 8);
        var sizes = zones.Select(x => AspectFit.Fit(x, 16.0 / 9, 2, 32)).ToArray();
        foreach (var r in CompactLayout.Arrange(resolved, zones, sizes, 8))
            Check(r.X >= resolved.X && r.Y >= resolved.Y && r.Right <= resolved.Right && r.Bottom <= resolved.Bottom, "All layouts compact inside selected region");
    }
    Check(new TilingRegion { Width = work.Width, Height = work.Height }.Resolve(work) == work, "Whole-area region matches existing geometry");
}
Reject(() => new TilingRegion { X = -1, Width = 100, Height = 100 }.Resolve(area), "Reject negative region offset");
Reject(() => new TilingRegion { Width = 0, Height = 100 }.Resolve(area), "Reject empty region");
Reject(() => new TilingRegion { X = int.MaxValue, Width = int.MaxValue, Height = 100 }.Resolve(area), "Region bounds cannot overflow");
Reject(() => new TilingRegion { Width = area.Width + 1, Height = 100 }.Resolve(area), "Reject region outside new monitor");
Console.WriteLine($"PASS: {checks:N0} layout/aspect/compaction/region assertions.");
