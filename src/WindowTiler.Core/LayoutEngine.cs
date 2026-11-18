namespace WindowTiler.Core;

public enum LayoutKind { Grid, Horizontal, Vertical, MainLeft, MainRight, MainTop, MainBottom, Custom }

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public sealed class LayoutNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Axis { get; set; } = "X";
    public double Ratio { get; set; } = .5;
    public LayoutNode? First { get; set; }
    public LayoutNode? Second { get; set; }
    public bool IsLeaf => First is null && Second is null;
    public LayoutNode Clone() => new() { Id = Id, Axis = Axis, Ratio = Ratio, First = First?.Clone(), Second = Second?.Clone() };
    public IEnumerable<LayoutNode> Leaves() => IsLeaf ? [this] : First!.Leaves().Concat(Second!.Leaves());
    public LayoutNode? Find(string id) => Id == id ? this : First?.Find(id) ?? Second?.Find(id);
    public LayoutNode? ParentOf(string id) => First?.Id == id || Second?.Id == id ? this : First?.ParentOf(id) ?? Second?.ParentOf(id);
    public void Validate(int depth = 0)
    {
        if (depth > 12 || string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("布局分区数据无效。");
        if (IsLeaf) return;
        if (First is null || Second is null || Axis is not ("X" or "Y") || !double.IsFinite(Ratio) || Ratio < .1 || Ratio > .9)
            throw new ArgumentException("布局分区比例必须在 10% 到 90% 之间。");
        First.Validate(depth + 1); Second.Validate(depth + 1);
    }
}

public sealed class LayoutTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新布局";
    public LayoutNode Root { get; set; } = new();
    public override string ToString() => Name;
}

public static class LayoutEngine
{
    public static IReadOnlyList<PixelRect> Arrange(PixelRect area, int count, LayoutKind kind, int gap = 8,
        double mainRatio = .6, LayoutNode? template = null)
    {
        if (count < 0 || count > 100) throw new ArgumentOutOfRangeException(nameof(count), "窗口数量必须在 0 到 100 之间。");
        if (count == 0) return [];
        if (gap < 0 || gap > 100 || area.Width <= 0 || area.Height <= 0) throw new ArgumentException("屏幕尺寸或间距无效。");
        if (!double.IsFinite(mainRatio) || mainRatio < .1 || mainRatio > .9) throw new ArgumentException("主窗口比例无效。");
        List<PixelRect> zones = [];
        if (kind == LayoutKind.Custom)
        {
            if (template is null) throw new ArgumentException("请先选择自定义模板。");
            template.Validate();
            zones = Partition(template, area).Select(x => x.Rect).ToList();
            if (count > zones.Count) throw new ArgumentException($"模板只有 {zones.Count} 个分区，但分配了 {count} 个窗口。");
            zones = zones.Take(count).ToList();
        }
        else if (count == 1) zones.Add(area);
        else if (kind == LayoutKind.Horizontal) zones.AddRange(Strip(area, count, true));
        else if (kind == LayoutKind.Vertical) zones.AddRange(Strip(area, count, false));
        else if (kind is LayoutKind.MainLeft or LayoutKind.MainRight or LayoutKind.MainTop or LayoutKind.MainBottom)
        {
            bool horizontal = kind is LayoutKind.MainLeft or LayoutKind.MainRight;
            bool reverse = kind is LayoutKind.MainRight or LayoutKind.MainBottom;
            var parts = Split(area, horizontal, reverse ? 1 - mainRatio : mainRatio);
            var main = reverse ? parts.Second : parts.First;
            var rest = reverse ? parts.First : parts.Second;
            zones.Add(main);
            zones.AddRange(Strip(rest, count - 1, !horizontal));
        }
        else
        {
            // Choose a grid whose cells approach a landscape window aspect ratio.
            int rows = Enumerable.Range(1, count).OrderBy(r =>
                Math.Abs(Math.Log((double)area.Width / Math.Ceiling((double)count / r) / ((double)area.Height / r) / 1.5))
                + (r * Math.Ceiling((double)count / r) - count) * .08).First();
            int basic = count / rows, extra = count % rows;
            var bands = Strip(area, rows, false).ToList();
            for (int r = 0; r < rows; r++) zones.AddRange(Strip(bands[r], basic + (r < extra ? 1 : 0), true));
        }
        // Outer margins are a full gap; shared boundaries contribute half from each side.
        return zones.Select(z =>
        {
            int left = z.X == area.X ? gap : gap / 2;
            int top = z.Y == area.Y ? gap : gap / 2;
            int right = z.Right == area.Right ? gap : gap - gap / 2;
            int bottom = z.Bottom == area.Bottom ? gap : gap - gap / 2;
            var result = new PixelRect(z.X + left, z.Y + top, z.Width - left - right, z.Height - top - bottom);
            if (result.Width < 40 || result.Height < 40) throw new ArgumentException("分区过小，请减少窗口数量、减小间距或调整模板。");
            return result;
        }).ToList();
    }

    public static IEnumerable<(LayoutNode Node, PixelRect Rect)> Partition(LayoutNode node, PixelRect area)
    {
        if (node.IsLeaf) { yield return (node, area); yield break; }
        var parts = Split(area, node.Axis == "X", node.Ratio);
        foreach (var item in Partition(node.First!, parts.First)) yield return item;
        foreach (var item in Partition(node.Second!, parts.Second)) yield return item;
    }

    public static (PixelRect First, PixelRect Second) Split(PixelRect rect, bool horizontal, double ratio)
    {
        int cut = (int)Math.Round((horizontal ? rect.Width : rect.Height) * ratio);
        return horizontal
            ? (new(rect.X, rect.Y, cut, rect.Height), new(rect.X + cut, rect.Y, rect.Width - cut, rect.Height))
            : (new(rect.X, rect.Y, rect.Width, cut), new(rect.X, rect.Y + cut, rect.Width, rect.Height - cut));
    }

    private static IEnumerable<PixelRect> Strip(PixelRect area, int count, bool horizontal)
    {
        for (int i = 0; i < count; i++)
        {
            int start = (int)((long)(horizontal ? area.Width : area.Height) * i / count);
            int end = (int)((long)(horizontal ? area.Width : area.Height) * (i + 1) / count);
            yield return horizontal ? new(area.X + start, area.Y, end - start, area.Height)
                : new(area.X, area.Y + start, area.Width, end - start);
        }
    }
}
