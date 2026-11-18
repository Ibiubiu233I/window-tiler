using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using LayoutKind = System.Runtime.InteropServices.LayoutKind;

namespace WindowTiler.App;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly PixelRect Pixels => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Placement
    {
        public int Length, Flags, ShowCmd;
        public Point MinPosition, MaxPosition;
        public Rect NormalPosition;
        public static Placement Create() => new() { Length = Marshal.SizeOf<Placement>() };
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    internal delegate bool EnumWindowsProc(nint hwnd, nint param);
    internal delegate bool MonitorProc(nint monitor, nint dc, nint rect, nint param);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, nint param);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorProc callback, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder title, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] internal static extern nint GetClassLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowPlacement(nint hwnd, ref Placement placement);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPlacement(nint hwnd, in Placement placement);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint SendMessageTimeout(nint hwnd, uint msg, nuint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] internal static extern int GetDwmRect(nint hwnd, int attribute, out Rect value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] internal static extern int GetDwmInt(nint hwnd, int attribute, out int value, int size);

    internal static List<DisplayItem> Displays()
    {
        var list = new List<DisplayItem>();
        EnumDisplayMonitors(0, 0, (handle, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (GetMonitorInfo(handle, ref info)) list.Add(new DisplayItem(info.Device, info.Monitor.Pixels, info.Work.Pixels, (info.Flags & 1) != 0));
            return true;
        }, 0);
        list = list.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Bounds.X).ThenBy(x => x.Bounds.Y).ToList();
        for (int i = 0; i < list.Count; i++) list[i].Number = i + 1;
        return list;
    }

    internal static List<WindowItem> Windows(IReadOnlyList<DisplayItem> displays)
    {
        List<WindowItem> list = [];
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId || pid == 0) return true;
            long style = GetWindowLongPtr(hwnd, -20).ToInt64();
            if ((style & 0x80) != 0 || ((style & 0x40000) == 0 && GetWindow(hwnd, 4) != 0)) return true;
            if (GetDwmInt(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var title = new StringBuilder(1024);
            if (GetWindowText(hwnd, title, title.Capacity) == 0) return true;
            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                long started = 0;
                try { started = process.StartTime.ToUniversalTime().Ticks; } catch { /* Protected process: use PID and class identity. */ }
                var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
                GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitorInfo);
                list.Add(new WindowItem { Handle = hwnd, ProcessId = (int)pid, ProcessStarted = started,
                    ClassName = className.ToString(), Title = title.ToString(), ProcessName = process.ProcessName,
                    DisplayKey = displays.FirstOrDefault(x => x.Key == monitorInfo.Device)?.Key ?? displays.FirstOrDefault()?.Key ?? "",
                    Icon = WindowIcon(hwnd), State = IsIconic(hwnd) ? "已最小化" : IsZoomed(hwnd) ? "已最大化" : "已打开" });
            }
            catch (ArgumentException) { } catch (System.ComponentModel.Win32Exception) { }
            return true;
        }, 0);
        return list;
    }

    private static ImageSource? WindowIcon(nint hwnd)
    {
        try
        {
            SendMessageTimeout(hwnd, 0x7F, 2, 0, 2, 40, out nint icon);
            if (icon == 0) icon = GetClassLongPtr(hwnd, -34);
            if (icon == 0) icon = GetClassLongPtr(hwnd, -14);
            if (icon == 0) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(24, 24));
            source.Freeze(); return source;
        }
        catch { return null; }
    }

    internal static bool SameWindow(WindowItem window)
    {
        if (!IsWindow(window.Handle)) return false;
        GetWindowThreadProcessId(window.Handle, out uint pid);
        if (pid != window.ProcessId) return false;
        var name = new StringBuilder(256);
        GetClassName(window.Handle, name, name.Capacity);
        if (name.ToString() != window.ClassName) return false;
        if (window.ProcessStarted != 0)
        {
            try { using var p = Process.GetProcessById((int)pid); return p.StartTime.ToUniversalTime().Ticks == window.ProcessStarted; }
            catch { return false; }
        }
        return true;
    }

    internal static PixelRect VisibleRect(nint hwnd)
    {
        if (GetDwmRect(hwnd, 9, out var frame, Marshal.SizeOf<Rect>()) == 0 && frame.Right > frame.Left) return frame.Pixels;
        GetWindowRect(hwnd, out var rect); return rect.Pixels;
    }

    internal static bool ReadAspect(WindowItem window, bool captureRatio)
    {
        if (!SameWindow(window) || IsIconic(window.Handle) || IsZoomed(window.Handle) ||
            !GetClientRect(window.Handle, out var client) || client.Right <= 0 || client.Bottom <= 0) return false;
        var visible = VisibleRect(window.Handle);
        if (visible.Width <= 0 || visible.Height <= 0) return false;
        if (captureRatio) window.ClientAspectRatio = (double)client.Right / client.Bottom;
        window.FrameWidth = Math.Max(0, visible.Width - client.Right);
        window.FrameHeight = Math.Max(0, visible.Height - client.Bottom);
        return true;
    }

    internal static bool RaiseWindow(nint hwnd)
    {
        // Raise within the window's existing band; ordinary windows remain ordinary.
        bool alreadyTopmost = (GetWindowLongPtr(hwnd, -20).ToInt64() & 8) != 0;
        return SetWindowPos(hwnd, alreadyTopmost ? -1 : 0, 0, 0, 0, 0,
            0x0001 | 0x0002 | 0x0010 | 0x0200 | 0x4000);
    }

    internal static bool IsTopmost(nint hwnd) => (GetWindowLongPtr(hwnd, -20).ToInt64() & 8) != 0;

    internal static List<nint> VisibleZOrder()
    {
        var order = new List<nint>();
        EnumWindows((handle, _) =>
        {
            if (IsWindowVisible(handle) && (GetDwmInt(handle, 14, out int cloaked, sizeof(int)) != 0 || cloaked == 0)) order.Add(handle);
            return true;
        }, 0);
        return order;
    }

    internal static bool GroupIsInFront(IReadOnlyList<WindowItem> windows)
    {
        var order = VisibleZOrder();
        if (windows.Any(x => !SameWindow(x) || !order.Contains(x.Handle))) return false;
        var selected = windows.Select(x => x.Handle).ToHashSet();
        foreach (bool topmost in new[] { false, true })
        {
            var indices = windows.Where(x => IsTopmost(x.Handle) == topmost).Select(x => order.IndexOf(x.Handle)).ToArray();
            if (indices.Length == 0) continue;
            if (!indices.SequenceEqual(indices.OrderBy(x => x))) return false;
            foreach (var other in order.Take(indices[^1]))
            {
                if (selected.Contains(other) || IsTopmost(other) != topmost || (GetWindowLongPtr(other, -20).ToInt64() & 0x80) != 0) continue;
                var title = new StringBuilder(256);
                if (GetWindowText(other, title, title.Capacity) > 0) return false;
            }
        }
        return true;
    }

    private static async Task<bool> WaitForBandAsync(WindowItem window, bool topmost)
    {
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(20);
            if (!SameWindow(window)) return false;
            if (IsTopmost(window.Handle) == topmost && SendMessageTimeout(window.Handle, 0, 0, 0, 2, 40, out _) != 0) return true;
        }
        return false;
    }

    internal static async Task<bool> RaiseGroupAsync(IReadOnlyList<WindowItem> windows)
    {
        var originalBands = windows.ToDictionary(x => x, x => IsTopmost(x.Handle));
        if (windows.FirstOrDefault(x => SameWindow(x)) is { } first) SetForegroundWindow(first.Handle);
        if (GroupIsInFront(windows)) return true;
        const uint flags = 0x0001 | 0x0002 | 0x0010 | 0x0200 | 0x4000;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            bool ok = true;
            // Await each target's Z-order update: separate application threads may
            // process async positioning requests in a different order otherwise.
            foreach (var window in windows.Reverse())
            {
                if (!SameWindow(window)) { ok = false; continue; }
                bool promoted = false, restored = originalBands[window];
                try
                {
                    promoted = SetWindowPos(window.Handle, -1, 0, 0, 0, 0, flags) && await WaitForBandAsync(window, true);
                }
                finally
                {
                    // Always queue demotion for originally ordinary windows, even
                    // if promotion timed out. Do not leave a target permanently pinned.
                    if (!originalBands[window] && SameWindow(window))
                        restored = SetWindowPos(window.Handle, -2, 0, 0, 0, 0, flags) && await WaitForBandAsync(window, false);
                }
                ok &= promoted && restored;
            }
            await Task.Delay(80);
            if (ok && GroupIsInFront(windows)) return true;
        }
        return false;
    }
}
