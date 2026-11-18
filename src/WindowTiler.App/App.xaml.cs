using System.Diagnostics;
using System.Threading;
using Forms = System.Windows.Forms;

namespace WindowTiler.App;

public partial class App : Application
{
    private Mutex? mutex;
    private EventWaitHandle? showEvent;
    private RegisteredWaitHandle? showWait;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private bool explainedTray, exiting;
    internal HotkeyService? Hotkeys { get; private set; }
    internal static bool SmokeMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Initialize();
        if (e.Args.Contains("--front-fixture")) { SmokeTests.StartFrontFixture(); return; }
        if (e.Args.Contains("--fixture")) { SmokeTests.StartFixtures(); return; }
        SmokeMode = e.Args.Contains("--smoke-test");
        if (!SmokeMode)
        {
            mutex = new Mutex(true, @"Local\WindowTiler.App.30DB49E2", out bool created);
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WindowTiler.Show.30DB49E2");
            if (!created) { showEvent.Set(); Shutdown(); return; }
        }
        var window = new MainWindow(); MainWindow = window;
        window.Show();
        if (SmokeMode) { _ = SmokeTests.RunAsync(window); return; }
        Hotkeys = new HotkeyService(window);
        if (!Hotkeys.Register(window.Settings.PanelHotkey, window.Settings.ApplyHotkey, window.Settings.UndoHotkey, out string error))
            window.Dispatcher.InvokeAsync(() => { window.ShortcutText.Text = error; window.ShortcutText.Foreground = ThemeService.Brush("ErrorText"); window.ShowPanel(); }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        CreateTray(window);
        showWait = ThreadPool.RegisterWaitForSingleObject(showEvent!, (_, _) => Dispatcher.InvokeAsync(window.ShowPanel), null, Timeout.Infinite, false);
        if (e.Args.Contains("--tray")) window.Hide();
    }
    private void CreateTray(MainWindow window)
    {
        trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "铺窗 · Window Tiler", Visible = true };
        tray.DoubleClick += (_, _) => window.ShowPanel();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开操作面板", null, (_, _) => window.ShowPanel());
        menu.Items.Add("平铺当前所选窗口", null, async (_, _) => { await window.ApplyAsync(); NotifyResult(window); });
        menu.Items.Add("恢复上次位置", null, async (_, _) => { await window.UndoAsync(); NotifyResult(window); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出铺窗", null, (_, _) => ExitApp());
        tray.ContextMenuStrip = menu;
        menu.Renderer = new Forms.ToolStripProfessionalRenderer(new TrayThemeColors());
        ThemeService.Changed += UpdateTrayTheme;
        UpdateTrayTheme();
    }
    private void UpdateTrayTheme()
    {
        if (tray?.ContextMenuStrip is not { } menu) return;
        menu.BackColor = TrayThemeColors.Get("Surface");
        menu.ForeColor = TrayThemeColors.Get("TextPrimary");
        menu.Invalidate();
    }
    private sealed class TrayThemeColors : Forms.ProfessionalColorTable
    {
        internal static System.Drawing.Color Get(string key)
        {
            var c = ThemeService.Brush(key).Color;
            return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
        }
        public override System.Drawing.Color ToolStripDropDownBackground => Get("Surface");
        public override System.Drawing.Color ImageMarginGradientBegin => Get("Surface");
        public override System.Drawing.Color ImageMarginGradientMiddle => Get("Surface");
        public override System.Drawing.Color ImageMarginGradientEnd => Get("Surface");
        public override System.Drawing.Color MenuItemSelected => Get("SelectionBackground");
        public override System.Drawing.Color MenuItemBorder => Get("SelectionBackground");
        public override System.Drawing.Color MenuBorder => Get("Border");
        public override System.Drawing.Color SeparatorDark => Get("Border");
        public override System.Drawing.Color SeparatorLight => Get("Surface");
    }
    internal void NotifyResult(MainWindow window) { if (!window.IsVisible) tray?.ShowBalloonTip(2500, "铺窗", window.StatusText.Text, Forms.ToolTipIcon.Info); }
    internal void ExplainTrayOnce()
    {
        if (explainedTray) return; explainedTray = true;
        tray?.ShowBalloonTip(2500, "铺窗仍在运行", "双击托盘图标可打开面板，右键菜单可退出。", Forms.ToolTipIcon.Info);
    }
    internal void ExitApp()
    {
        if (exiting) return;
        if (MainWindow is MainWindow window)
        {
            if (window.IsBusy) { window.ShowPanel(); window.Status("正在调整窗口，完成后可退出。"); return; }
            window.ExitRequested = true; window.SaveSettings();
        }
        exiting = true; Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        ThemeService.Changed -= UpdateTrayTheme;
        ThemeService.Shutdown();
        Hotkeys?.Dispose();
        if (tray is not null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        trayIcon?.Dispose(); showWait?.Unregister(null); showEvent?.Dispose(); mutex?.Dispose(); base.OnExit(e);
    }
}
