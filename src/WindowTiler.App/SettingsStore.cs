using System.Text.Json;

namespace WindowTiler.App;

internal static class SettingsStore
{
    internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowTiler");
    internal static string FilePath => Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    internal static AppSettings Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(FilePath)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? throw new JsonException();
            if (!Enum.IsDefined(settings.Theme)) settings.Theme = AppTheme.Light;
            if (settings.Templates is null || settings.Displays is null || settings.Templates.Count > 100) throw new JsonException();
            foreach (var template in settings.Templates)
            {
                if (template is null || template.Root is null || string.IsNullOrWhiteSpace(template.Name) || string.IsNullOrWhiteSpace(template.Id)) throw new JsonException();
                template.Root.Validate();
                if (template.Root.Leaves().Count() > 16) throw new JsonException();
            }
            if (settings.Templates.Select(x => x.Id).Distinct().Count() != settings.Templates.Count) throw new JsonException();
            if (settings.Regions is null || settings.Regions.Count > 100) throw new JsonException();
            foreach (var region in settings.Regions) { if (region is null) throw new JsonException(); region.Validate(); }
            if (settings.Regions.Select(x => x.Id).Distinct().Count() != settings.Regions.Count) throw new JsonException();
            foreach (var layout in settings.Displays.Values)
                if (layout is null || !Enum.IsDefined(layout.Kind) || layout.Gap is < 0 or > 48 || !double.IsFinite(layout.MainRatio) || layout.MainRatio is < .1 or > .9) throw new JsonException();
            if (settings.SingleDisplayLayout is { } single && (!Enum.IsDefined(single.Kind) || single.Gap is < 0 or > 48 || !double.IsFinite(single.MainRatio) || single.MainRatio is < .1 or > .9)) throw new JsonException();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            string backup = FilePath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try { File.Copy(FilePath, backup); warning = "设置文件无法读取，已备份并使用默认设置。"; }
            catch { warning = "设置文件无法读取，使用默认设置；原文件未修改。"; }
            return new();
        }
    }
    internal static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, FilePath, true);
    }
}
