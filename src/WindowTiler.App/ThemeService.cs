using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Windows.Data;
using System.Windows.Interop;

namespace WindowTiler.App;

[JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
public enum AppTheme { System, Light, Dark }

internal static class ThemeService
{
    private sealed class Swatch : Observable
    {
        private Color color;
        public Color Color { get => color; set => Set(ref color, value); }
    }

    private static readonly Dictionary<string, (string Light, string Dark)> Colors = new()
    {
        ["WindowBackground"] = ("#F3F5FA", "#10151F"),
        ["Surface"] = ("#FFFFFF", "#1B2331"),
        ["SurfaceSubtle"] = ("#F8FAFD", "#202A3A"),
        ["SurfaceInset"] = ("#F3F6FC", "#141D2B"),
        ["TextPrimary"] = ("#1E293B", "#E6ECF6"),
        ["TextSecondary"] = ("#64748B", "#A5B3C9"),
        ["TextMuted"] = ("#7B899F", "#93A3BD"),
        ["FooterText"] = ("#A0ABBE", "#78879E"),
        ["Border"] = ("#DCE3EF", "#364359"),
        ["BorderSubtle"] = ("#E4E9F2", "#2B374B"),
        ["HoverBorder"] = ("#A8BCEB", "#627AA4"),
        ["HoverBackground"] = ("#F1F5FC", "#2B3A53"),
        ["SelectionBackground"] = ("#EAF0FF", "#293F67"),
        ["SelectionHover"] = ("#DEE8FF", "#354F7C"),
        ["FocusRing"] = ("#DFE8FF", "#2D4266"),
        ["Accent"] = ("#305CE8", "#91B3FF"),
        ["AccentFill"] = ("#305CE8", "#416BDD"),
        ["OnAccent"] = ("#FFFFFF", "#FFFFFF"),
        ["ResultText"] = ("#4164AA", "#9BBEFF"),
        ["ErrorText"] = ("#B54444", "#FF9A9A"),
        ["ZonePrimary"] = ("#DDE7FF", "#2B4064"),
        ["ZoneSecondary"] = ("#E8EDF8", "#25334B"),
        ["ZoneBorder"] = ("#B8C9F2", "#4A648F"),
        ["RegionScreen"] = ("#D9E1EB", "#344256"),
        ["RegionScreenBorder"] = ("#8295AD", "#71829A"),
        ["RegionSelection"] = ("#AEC9FF", "#244A7B"),
        ["TrackBackground"] = ("#DCE3EF", "#3A4860")
    };
    private static readonly Dictionary<string, Swatch> swatches = [];
    private static bool initialized;
    internal static AppTheme Mode { get; private set; } = AppTheme.Light;
    internal static bool IsDark { get; private set; }
    internal static event Action? Changed;
    internal static Func<bool> SystemThemeReader { get; set; } = ReadSystemDark;

    internal static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        foreach (var key in Colors.Keys)
        {
            var swatch = new Swatch();
            var brush = new SolidColorBrush();
            // Bound brushes cannot be frozen by a template, and also update code-created visuals.
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(Swatch.Color)) { Source = swatch, Mode = BindingMode.OneWay });
            swatches.Add(key, swatch); Application.Current.Resources[key] = brush;
        }
        Apply(AppTheme.Light);
        SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
    }
    internal static SolidColorBrush Brush(string key) => (SolidColorBrush)Application.Current.Resources[key];
    internal static void Apply(AppTheme mode)
    {
        Mode = Enum.IsDefined(mode) ? mode : AppTheme.System;
        IsDark = Mode == AppTheme.Dark || Mode == AppTheme.System && SystemThemeReader();
        foreach (var pair in Colors)
            swatches[pair.Key].Color = (Color)ColorConverter.ConvertFromString(IsDark ? pair.Value.Dark : pair.Value.Light);
        foreach (Window window in Application.Current.Windows) UpdateTitleBar(window);
        Changed?.Invoke();
    }
    internal static void RefreshSystemTheme()
    {
        if (Mode == AppTheme.System) Apply(AppTheme.System);
    }
    private static bool ReadSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }
    private static void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
            dispatcher.BeginInvoke(new Action(RefreshSystemTheme));
    }
    internal static void Track(Window window)
    {
        window.SourceInitialized += (_, _) => UpdateTitleBar(window);
    }
    private static void UpdateTitleBar(Window window)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        int dark = IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    internal static void Shutdown()
    {
        if (initialized) SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
    }
}
