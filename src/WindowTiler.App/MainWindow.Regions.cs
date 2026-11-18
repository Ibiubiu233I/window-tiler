namespace WindowTiler.App;

public partial class MainWindow
{
    internal PixelRect TilingArea(DisplayItem display, DisplayLayout layout) => !layout.UseRegion ? display.Work :
        (Settings.Regions.FirstOrDefault(x => x.Id == layout.RegionId) ?? throw new ArgumentException("请先新建或选择一个区域方案。")) .Resolve(display.Work);

    private void LoadRegionOptions()
    {
        RegionModeCombo.SelectedIndex = ActiveLayout?.UseRegion == true ? 1 : 0;
        RegionCombo.SelectedItem = Settings.Regions.FirstOrDefault(x => x.Id == ActiveLayout?.RegionId);
        RegionPanel.Visibility = ActiveLayout?.UseRegion == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateRegionHint();
    }
    private void UpdateRegionHint()
    {
        if (ActiveDisplay is not { } display || ActiveLayout is not { } layout) return;
        try
        {
            var area = TilingArea(display, layout);
            RegionHint.Text = layout.UseRegion ? $"左 {area.X - display.Work.X} · 上 {area.Y - display.Work.Y} · {area.Width} × {area.Height} px" : "全屏使用屏幕可用范围，自动避开任务栏。";
        }
        catch (ArgumentException ex) { RegionHint.Text = ex.Message; }
    }
    private void RegionMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ActiveLayout is not { } layout) return;
        layout.UseRegion = RegionModeCombo.SelectedIndex == 1;
        if (layout.UseRegion && layout.RegionId is null && Settings.Regions.Count > 0)
        {
            layout.RegionId = Settings.Regions[0].Id;
            loading = true; RegionCombo.SelectedItem = Settings.Regions[0]; loading = false;
        }
        RegionPanel.Visibility = layout.UseRegion ? Visibility.Visible : Visibility.Collapsed;
        UpdateRegionHint(); RenderPreview(); SaveSettings();
    }
    private void Region_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ActiveLayout is not { } layout) return;
        layout.RegionId = (RegionCombo.SelectedItem as TilingRegion)?.Id;
        UpdateRegionHint(); RenderPreview(); SaveSettings();
    }
    internal void StoreRegion(TilingRegion region)
    {
        var index = Settings.Regions.FindIndex(x => x.Id == region.Id);
        loading = true;
        if (index < 0) Settings.Regions.Add(region); else Settings.Regions[index] = region;
        RegionCombo.Items.Refresh();
        if (ActiveLayout is { } layout) { layout.UseRegion = true; layout.RegionId = region.Id; }
        LoadRegionOptions(); loading = false;
        RenderPreview(); SaveSettings();
    }
    private void EditRegion(TilingRegion? source, bool copy = false)
    {
        if (IsBusy || ActiveDisplay is not { } display) return;
        if ((source is null || copy) && Settings.Regions.Count >= 100) { Status("最多保存 100 个区域方案，请先删除不再使用的方案。"); return; }
        var editor = new RegionEditor(display, source, copy) { Owner = this };
        if (editor.ShowDialog() == true) StoreRegion(editor.Result!);
    }
    private void NewRegion_Click(object sender, RoutedEventArgs e) => EditRegion(null);
    private void EditRegion_Click(object sender, RoutedEventArgs e) { if (RegionCombo.SelectedItem is TilingRegion region) EditRegion(region); else Status("请先选择区域方案。"); }
    private void CopyRegion_Click(object sender, RoutedEventArgs e) { if (RegionCombo.SelectedItem is TilingRegion region) EditRegion(region, true); else Status("请先选择区域方案。"); }
    internal void RemoveRegion(TilingRegion region)
    {
        loading = true;
        foreach (var layout in Settings.Displays.Values.Concat(Settings.SingleDisplayLayout is { } single ? new[] { single } : Array.Empty<DisplayLayout>()))
            if (layout.RegionId == region.Id) { layout.UseRegion = false; layout.RegionId = null; }
        Settings.Regions.Remove(region); RegionCombo.Items.Refresh(); LoadRegionOptions(); loading = false;
        RenderPreview(); SaveSettings();
    }
    private void DeleteRegion_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || RegionCombo.SelectedItem is not TilingRegion region) return;
        if (Dialogs.Confirm(this, "删除区域方案", $"删除“{region.Name}”？使用此方案的屏幕将切回全屏。")) RemoveRegion(region);
    }
}
