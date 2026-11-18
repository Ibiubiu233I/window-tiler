using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WindowTiler.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }
}

public sealed class WindowItem : Observable
{
    public nint Handle { get; init; }
    public int ProcessId { get; init; }
    public long ProcessStarted { get; init; }
    public string ClassName { get; init; } = "";
    public string Title { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public ImageSource? Icon { get; init; }
    private string state = "";
    public string State { get => state; set => Set(ref state, value); }
    private bool selected;
    public bool IsSelected { get => selected; set => Set(ref selected, value); }
    private string displayKey = "";
    public string DisplayKey { get => displayKey; set => Set(ref displayKey, value); }
    private string result = "";
    public string Result { get => result; set => Set(ref result, value); }
    private bool keepAspectRatio;
    public bool KeepAspectRatio { get => keepAspectRatio; set => Set(ref keepAspectRatio, value); }
    internal double ClientAspectRatio { get; set; }
    internal int FrameWidth { get; set; }
    internal int FrameHeight { get; set; }
    internal PixelRect? LastFitZone { get; set; }
    internal PixelRect? LastFitSize { get; set; }
}

public sealed class DisplayItem(string key, PixelRect bounds, PixelRect work, bool primary)
{
    public string Key { get; } = key;
    public PixelRect Bounds { get; } = bounds;
    public PixelRect Work { get; } = work;
    public bool IsPrimary { get; } = primary;
    public int Number { get; set; }
    public string Label => $"显示器 {Number}{(IsPrimary ? " · 主屏" : "")}  {Bounds.Width} × {Bounds.Height}";
    public override string ToString() => Label;
}

public sealed class DisplayLayout
{
    public LayoutKind Kind { get; set; } = LayoutKind.Grid;
    public int Gap { get; set; } = 8;
    public double MainRatio { get; set; } = .6;
    public string? TemplateId { get; set; }
    public bool UseRegion { get; set; }
    public string? RegionId { get; set; }
    public DisplayLayout Clone() => new() { Kind = Kind, Gap = Gap, MainRatio = MainRatio, TemplateId = TemplateId, UseRegion = UseRegion, RegionId = RegionId };
}

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Light;
    public bool GroupByDisplay { get; set; }
    public DisplayLayout? SingleDisplayLayout { get; set; }
    public List<LayoutTemplate> Templates { get; set; } = [];
    public List<TilingRegion> Regions { get; set; } = [];
    public Dictionary<string, DisplayLayout> Displays { get; set; } = [];
    public string PanelHotkey { get; set; } = "Ctrl+Alt+Space";
    public string ApplyHotkey { get; set; } = "Ctrl+Alt+Enter";
    public string UndoHotkey { get; set; } = "Ctrl+Alt+Back";
    public bool MinimizeToTray { get; set; } = true;
}
