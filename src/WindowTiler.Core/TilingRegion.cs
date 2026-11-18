namespace WindowTiler.Core;

public sealed class TilingRegion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "我的区域";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Name.Length > 40 ||
            X < 0 || Y < 0 || Width <= 0 || Height <= 0)
            throw new ArgumentException("请填写方案名称、非负位置和大于 0 的宽高。");
    }
    public PixelRect Resolve(PixelRect work)
    {
        Validate();
        if ((long)X + Width > work.Width || (long)Y + Height > work.Height)
            throw new ArgumentException($"区域“{Name}”超出当前屏幕可用范围，请编辑区域或切换全屏。");
        return new(checked(work.X + X), checked(work.Y + Y), Width, Height);
    }
    public override string ToString() => Name;
}
