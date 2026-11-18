namespace WindowTiler.Core;

public static class CompactLayout
{
    public static IReadOnlyList<PixelRect> Arrange(PixelRect work, IReadOnlyList<PixelRect> zones, IReadOnlyList<PixelRect> sizes, int gap)
    {
        if (zones.Count != sizes.Count || gap < 0 || work.Width <= gap * 2 || work.Height <= gap * 2 ||
            sizes.Any(r => r.Width <= 0 || r.Height <= 0)) throw new ArgumentException("整组布局尺寸无效。");
        if (zones.Count == 0) return [];
        int[] x = new int[zones.Count], y = new int[zones.Count];
        // Preserve the original left/right and above/below relationships. Longest
        // paths remove empty strips without changing window sizes or causing overlap.
        foreach (int i in Enumerable.Range(0, zones.Count).OrderBy(i => zones[i].X))
            for (int j = 0; j < zones.Count; j++)
                if (j != i && zones[j].Right <= zones[i].X) x[i] = Math.Max(x[i], x[j] + sizes[j].Width + gap);
        foreach (int i in Enumerable.Range(0, zones.Count).OrderBy(i => zones[i].Y))
            for (int j = 0; j < zones.Count; j++)
                if (j != i && zones[j].Bottom <= zones[i].Y) y[i] = Math.Max(y[i], y[j] + sizes[j].Height + gap);
        int width = Enumerable.Range(0, zones.Count).Max(i => x[i] + sizes[i].Width);
        int height = Enumerable.Range(0, zones.Count).Max(i => y[i] + sizes[i].Height);
        if (width > work.Width - gap * 2 || height > work.Height - gap * 2) throw new ArgumentException("实际窗口尺寸无法整组放入屏幕。");
        int left = work.X + (work.Width - width) / 2, top = work.Y + (work.Height - height) / 2;
        return Enumerable.Range(0, zones.Count).Select(i => new PixelRect(left + x[i], top + y[i], sizes[i].Width, sizes[i].Height)).ToArray();
    }
}
