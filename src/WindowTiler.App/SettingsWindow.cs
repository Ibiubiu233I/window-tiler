using Microsoft.Win32;

namespace WindowTiler.App;

internal sealed class SettingsWindow : Window
{
    internal ComboBox ThemeSelector { get; } = new() { DisplayMemberPath = "Value", SelectedValuePath = "Key" };
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal SettingsWindow(AppSettings settings, HotkeyService? hotkeys)
    {
        Style = (Style)FindResource(typeof(Window));
        ThemeService.Track(this);
        Title = "铺窗设置"; Width = 540; Height = 600; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(28) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "让操作更顺手", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        var originalTheme = ThemeService.Mode;
        bool saved = false;
        var themeRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        themeRow.Children.Add(new TextBlock { Text = "外观主题", Width = 128, VerticalAlignment = VerticalAlignment.Center });
        ThemeSelector.ItemsSource = new Dictionary<AppTheme, string> { [AppTheme.Light] = "浅色（默认）", [AppTheme.System] = "跟随系统", [AppTheme.Dark] = "深色" };
        ThemeSelector.SelectedValue = settings.Theme;
        themeRow.Children.Add(ThemeSelector); panel.Children.Add(themeRow);
        panel.Children.Add(new TextBlock { Text = "默认使用浅色；选择“跟随系统”后随 Windows 自动切换。", Foreground = ThemeService.Brush("TextSecondary"), FontSize = 12, Margin = new Thickness(0, 0, 0, 20) });
        ThemeSelector.SelectionChanged += (_, _) => { if (ThemeSelector.SelectedValue is AppTheme mode) ThemeService.Apply(mode); };
        Closed += (_, _) => { if (!saved) ThemeService.Apply(originalTheme); };
        TextBox AddInput(string label, string value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            row.Children.Add(new TextBlock { Text = label, Width = 128, VerticalAlignment = VerticalAlignment.Center });
            var input = new TextBox { Text = value }; row.Children.Add(input); panel.Children.Add(row); return input;
        }
        var show = AddInput("呼出操作面板", settings.PanelHotkey);
        var apply = AddInput("平铺当前所选窗口", settings.ApplyHotkey);
        var undo = AddInput("恢复上次位置", settings.UndoHotkey);
        panel.Children.Add(new TextBlock { Text = "输入示例：Ctrl+Alt+Space、Ctrl+Shift+F8", Foreground = ThemeService.Brush("TextSecondary"), FontSize = 12, Margin = new Thickness(0, 0, 0, 20) });
        var tray = new CheckBox { Content = "关闭主窗口时驻留托盘", IsChecked = settings.MinimizeToTray, Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(tray);
        bool originalStartup = false;
        try { using var key = Registry.CurrentUser.OpenSubKey(RunKey); originalStartup = key?.GetValue("WindowTiler") is string; } catch { }
        var startup = new CheckBox { Content = "登录 Windows 后启动到托盘", IsChecked = originalStartup, Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(startup);
        var error = new TextBlock { Foreground = ThemeService.Brush("ErrorText"), TextWrapping = TextWrapping.Wrap, MinHeight = 48 }; panel.Children.Add(error);
        var save = new Button { Content = "保存设置", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true }; panel.Children.Add(save);
        panel.Children.Add(new TextBlock { Text = "铃彦姬！展示你真正的女性魅力吧", FontSize = 11,
            Foreground = ThemeService.Brush("FooterText"), TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 20, 0, 0) });
        save.Click += (_, _) =>
        {
            if (hotkeys is not null && !hotkeys.Register(show.Text.Trim(), apply.Text.Trim(), undo.Text.Trim(), out string message)) { error.Text = message; return; }
            try
            {
                if ((startup.IsChecked == true) != originalStartup)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                    if (startup.IsChecked == true) key.SetValue("WindowTiler", $"\"{Environment.ProcessPath}\" --tray"); else key.DeleteValue("WindowTiler", false);
                }
            }
            catch (Exception ex)
            {
                hotkeys?.Register(settings.PanelHotkey, settings.ApplyHotkey, settings.UndoHotkey, out _);
                error.Text = "开机启动设置失败：" + ex.Message; return;
            }
            settings.PanelHotkey = show.Text.Trim(); settings.ApplyHotkey = apply.Text.Trim(); settings.UndoHotkey = undo.Text.Trim(); settings.MinimizeToTray = tray.IsChecked == true;
            settings.Theme = ThemeSelector.SelectedValue is AppTheme mode ? mode : AppTheme.System;
            saved = true;
            DialogResult = true;
        };
    }
}
