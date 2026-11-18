namespace WindowTiler.Core;

public static class AspectFit
{
    // The ratio describes the client/content area. The visible title bar and
    // borders consume space but must not participate in that ratio.
    public static PixelRect Fit(PixelRect zone, double clientRatio, int frameWidth, int frameHeight)
    {
        if (!double.IsFinite(clientRatio) || clientRatio <= 0 || frameWidth < 0 || frameHeight < 0 ||
            zone.Width <= frameWidth || zone.Height <= frameHeight)
            throw new ArgumentException("分区太小或窗口比例无效。");
        int availableWidth = zone.Width - frameWidth, availableHeight = zone.Height - frameHeight;
        int width, height;
        if ((double)availableWidth / availableHeight > clientRatio)
        {
            height = availableHeight;
            width = (int)Math.Floor(height * clientRatio);
        }
        else
        {
            width = availableWidth;
            height = (int)Math.Floor(width / clientRatio);
        }
        if (width < 1 || height < 1) throw new ArgumentException("分区太小，无法保持窗口比例。");
        width += frameWidth; height += frameHeight;
        return new(zone.X + (zone.Width - width) / 2, zone.Y + (zone.Height - height) / 2, width, height);
    }
}
