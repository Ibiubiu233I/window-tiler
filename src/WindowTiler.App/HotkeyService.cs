using System.Windows.Input;
using System.Windows.Interop;

namespace WindowTiler.App;

internal sealed class HotkeyService : IDisposable
{
    private readonly nint handle;
    private readonly HwndSource source;
    private readonly MainWindow window;
    private readonly List<(int Id, uint Modifiers, uint Key)> registered = [];
    internal HotkeyService(MainWindow main)
    {
        window = main; handle = new WindowInteropHelper(main).EnsureHandle(); source = HwndSource.FromHwnd(handle); source.AddHook(Hook);
    }
    internal bool Register(string panel, string apply, string undo, out string error)
    {
        var next = new List<(int Id, uint Modifiers, uint Key)>();
        try
        {
            int id = 1;
            foreach (var text in new[] { panel, apply, undo })
            {
                var gesture = (KeyGesture?)new KeyGestureConverter().ConvertFromInvariantString(text);
                if (gesture is null || gesture.Key == Key.None || gesture.Modifiers == ModifierKeys.None) throw new ArgumentException("快捷键需要包含 Ctrl、Alt、Shift 或 Win 修饰键。");
                uint modifiers = 0x4000;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 1;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 2;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
                if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= 8;
                next.Add((id++, modifiers, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key)));
            }
            if (next.Select(x => (x.Modifiers, x.Key)).Distinct().Count() != next.Count) throw new ArgumentException("三个快捷键不能相同。");
        }
        catch (Exception ex) { error = "快捷键格式无效：" + ex.Message; return false; }
        var old = registered.ToList();
        foreach (var item in old) Native.UnregisterHotKey(handle, item.Id);
        registered.Clear();
        foreach (var item in next)
        {
            if (Native.RegisterHotKey(handle, item.Id, item.Modifiers, item.Key)) { registered.Add(item); continue; }
            foreach (var added in registered) Native.UnregisterHotKey(handle, added.Id);
            registered.Clear();
            foreach (var previous in old) if (Native.RegisterHotKey(handle, previous.Id, previous.Modifiers, previous.Key)) registered.Add(previous);
            error = $"第 {item.Id} 个快捷键被其他程序占用，请更换。" + (registered.Count != old.Count ? "部分原快捷键恢复失败，请重新设置。" : ""); return false;
        }
        error = ""; return true;
    }
    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0312)
        {
            handled = true;
            if ((int)wParam == 1) window.ShowPanel();
            else if ((int)wParam == 2) _ = ExecuteAsync(false);
            else if ((int)wParam == 3) _ = ExecuteAsync(true);
        }
        if (msg == 0x007E) window.Dispatcher.InvokeAsync(async () => await window.RefreshAsync());
        return 0;
    }
    private async Task ExecuteAsync(bool undo)
    {
        if (undo) await window.UndoAsync(); else await window.ApplyAsync();
        ((App)Application.Current).NotifyResult(window);
    }
    public void Dispose()
    {
        foreach (var item in registered) Native.UnregisterHotKey(handle, item.Id);
        registered.Clear(); source.RemoveHook(Hook);
    }
}
