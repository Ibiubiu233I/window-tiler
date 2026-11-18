namespace WindowTiler.App;

internal static class Dialogs
{
    internal static bool Confirm(Window owner, string title, string message)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 450, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Style = (Style)Application.Current.FindResource(typeof(Window)) };
        ThemeService.Track(dialog);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 24) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "取消", IsCancel = true });
        var confirm = new Button { Content = "删除", Style = (Style)Application.Current.FindResource("PrimaryButton"), Margin = new Thickness(0) };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(confirm); panel.Children.Add(buttons); dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }
}
