using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowTiler.App;

internal sealed record TilingGroup(PixelRect Work, int Gap, IReadOnlyList<(WindowItem Window, PixelRect Target)> Plan, bool Compact);

internal sealed class WindowManager
{
    private readonly List<(WindowItem Window, Native.Placement Placement)> undo = [];
    public bool CanUndo => undo.Count > 0;
    public bool Busy { get; private set; }
    public async Task<(int Success, int Total)> ApplyAsync(IReadOnlyList<(WindowItem Window, PixelRect Target)> plan,
        IReadOnlyList<TilingGroup>? groups = null, nint panel = 0)
    {
        if (Busy) throw new InvalidOperationException("正在调整窗口，请稍候。");
        Busy = true;
        var pending = new List<(WindowItem Window, PixelRect Target, Native.Placement Placement)>();
        var positioned = new List<WindowItem>();
        int success = 0;
        try
        {
            foreach (var (window, target) in plan)
            {
                if (!Native.SameWindow(window)) { window.Result = "窗口已关闭，请刷新"; continue; }
                var placement = Native.Placement.Create();
                if (!Native.GetWindowPlacement(window.Handle, ref placement)) { window.Result = "无法读取窗口位置"; continue; }
                pending.Add((window, target, placement));
                if (pending.Count == 1) undo.Clear();
                undo.Add((window, placement));
            }
            // Snapshot every target before moving any of them. Await independent
            // windows together, but keep continuations on the WPF dispatcher.
            using var limit = new System.Threading.SemaphoreSlim(8);
            var outcomes = await Task.WhenAll(pending.Select(async item =>
            {
                await limit.WaitAsync();
                try
                {
                    var (window, target, placement) = item;
                    if (Native.IsIconic(window.Handle) || Native.IsZoomed(window.Handle))
                        if (!await RestoreNormalAsync(window, placement)) return false;
                    if (!Native.SameWindow(window)) { window.Result = "窗口已关闭"; return false; }
                    bool matches = await PlaceInZoneAsync(window, target);
                    if (!matches && Native.SameWindow(window))
                    {
                        string failure = window.Result;
                        var restore = placement; restore.Flags |= 4;
                        bool restored = Native.SetWindowPlacement(window.Handle, in restore) && await ConfirmPlacementAsync(window, placement);
                        window.Result = failure + (restored ? "；已恢复原位置" : "；原位置未恢复，可点击恢复上次位置重试");
                    }
                    window.State = Native.IsIconic(window.Handle) ? "已最小化" : Native.IsZoomed(window.Handle) ? "已最大化" : "已打开";
                    if (matches) { window.LastFitZone = target; window.LastFitSize = Native.VisibleRect(window.Handle); }
                    return matches;
                }
                finally { limit.Release(); }
            }));
            for (int i = 0; i < pending.Count; i++)
                if (outcomes[i]) { success++; positioned.Add(pending[i].Window); }
            async Task<bool> MoveAndCheckAsync(WindowItem item, PixelRect target)
            {
                await limit.WaitAsync();
                try
                {
                    return await MoveVisibleAsync(item, target, false) && CloseRect(Native.VisibleRect(item.Handle), target);
                }
                finally { limit.Release(); }
            }
            foreach (var group in groups ?? [])
            {
                if (!group.Compact || group.Plan.Count < 2 || !group.Plan.Any(x => x.Window.KeepAspectRatio) ||
                    group.Plan.Any(x => !positioned.Contains(x.Window))) continue;
                var before = group.Plan.Select(x => Native.VisibleRect(x.Window.Handle)).ToArray();
                bool packed = true;
                try
                {
                    var targets = CompactLayout.Arrange(group.Work, group.Plan.Select(x => x.Target).ToArray(), before, group.Gap);
                    var moves = await Task.WhenAll(group.Plan.Select((item, i) => before[i] == targets[i]
                        ? Task.FromResult(true) : MoveAndCheckAsync(item.Window, targets[i])));
                    packed = moves.All(x => x);
                }
                catch (ArgumentException) { packed = false; }
                if (!packed)
                {
                    var restoredPositions = await Task.WhenAll(group.Plan.Select((item, i) => MoveAndCheckAsync(item.Window, before[i])));
                    for (int i = 0; i < before.Length; i++)
                    {
                        var item = group.Plan[i].Window;
                        bool restored = restoredPositions[i];
                        item.Result = restored ? "整组靠拢未完成，已回到适配位置；可重试或撤销" : "整组靠拢未完成，位置未恢复；请撤销重试";
                        positioned.Remove(item); success--;
                    }
                }
            }
            // Complete the layout first, then raise the selected group above other
            // normal windows. Reverse order leaves the first/main window at the front.
            if (positioned.Count > 0)
            {
                if (panel != 0 && Native.IsWindowVisible(panel)) Native.SetWindowPos(panel, 1, 0, 0, 0, 0, 0x0013 | 0x0200);
                bool raised = await Native.RaiseGroupAsync(positioned);
                if (!raised)
                    foreach (var item in positioned) { item.Result = "位置已调整，但整组前置未确认；请检查权限或重试"; success--; }
            }
            return (success, plan.Count);
        }
        finally { Busy = false; }
    }

    private static bool NormalWindow(WindowItem window) => Native.SameWindow(window) && !Native.IsIconic(window.Handle) && !Native.IsZoomed(window.Handle);
    private static bool CloseRect(PixelRect a, PixelRect b) => Math.Abs(a.X - b.X) <= 4 && Math.Abs(a.Y - b.Y) <= 4 &&
        Math.Abs(a.Width - b.Width) <= 4 && Math.Abs(a.Height - b.Height) <= 4;
    private static bool Inside(PixelRect a, PixelRect zone) => a.Width > 0 && a.Height > 0 &&
        a.X >= zone.X && a.Y >= zone.Y && a.Right <= zone.Right && a.Bottom <= zone.Bottom;
    private static bool HasRatio(WindowItem window, double ratio) => Native.GetClientRect(window.Handle, out var client) && client.Bottom > 0 &&
        Math.Abs(client.Right - client.Bottom * ratio) <= Math.Max(2, ratio * 2);

    private static async Task<bool> MoveVisibleAsync(WindowItem window, PixelRect target, bool resize = true)
    {
        if (!NormalWindow(window) || !Native.GetWindowRect(window.Handle, out var raw))
        { window.Result = "窗口已关闭或不处于普通状态"; return false; }
        var visible = Native.VisibleRect(window.Handle);
        if (visible == target && Native.SendMessageTimeout(window.Handle, 0, 0, 0, 2, 50, out _) != 0) return true;
        int left = visible.X - raw.Left, top = visible.Y - raw.Top;
        int right = raw.Right - visible.Right, bottom = raw.Bottom - visible.Bottom;
        if (!Native.SetWindowPos(window.Handle, 0, target.X - left, target.Y - top,
            target.Width + left + right, target.Height + top + bottom, 0x0004 | 0x0010 | 0x0200 | 0x4000 | (resize ? 0u : 1u)))
        {
            int error = Marshal.GetLastWin32Error();
            window.Result = error == 5 ? "权限不足：目标程序以管理员身份运行" : "移动失败：" + new Win32Exception(error).Message;
            return false;
        }
        // A window may correct its size after processing the original request.
        PixelRect? previous = null;
        int stable = 0;
        // Resizing retains the original stabilization window; position-only
        // compaction can finish sooner because it does not restart app layout.
        int interval = resize ? 60 : 20, requiredStable = resize ? 3 : 1;
        for (int attempt = 0; attempt < 720 / interval; attempt++)
        {
            await Task.Delay(interval);
            if (!NormalWindow(window)) { window.Result = "窗口已关闭或重新进入最大化／最小化状态"; return false; }
            var current = Native.VisibleRect(window.Handle);
            stable = current == previous ? stable + 1 : 0; previous = current;
            if (stable >= requiredStable && Native.SendMessageTimeout(window.Handle, 0, 0, 0, 2, 50, out _) != 0) return true;
        }
        window.Result = "窗口尺寸未稳定，可能响应缓慢";
        return false;
    }

    private static async Task<bool> PlaceInZoneAsync(WindowItem window, PixelRect zone)
    {
        if (!Native.ReadAspect(window, !window.KeepAspectRatio || window.ClientAspectRatio <= 0))
        { window.Result = "无法读取窗口内容尺寸"; return false; }
        double ratio = window.ClientAspectRatio;
        try
        {
            PixelRect desired = zone;
            // Recompute the visible frame after crossing the destination DPI boundary.
            for (int pass = 0; pass < 2; pass++)
            {
                if (!Native.ReadAspect(window, false)) { window.Result = "无法读取窗口内容尺寸"; return false; }
                desired = window.KeepAspectRatio ? AspectFit.Fit(zone, ratio, window.FrameWidth, window.FrameHeight) : zone;
                if (!await MoveVisibleAsync(window, desired)) return false;
                if (!Native.ReadAspect(window, false)) { window.Result = "无法读取窗口内容尺寸"; return false; }
                var corrected = window.KeepAspectRatio ? AspectFit.Fit(zone, ratio, window.FrameWidth, window.FrameHeight) : zone;
                // Retry only if crossing DPI changed the frame calculation. A
                // second identical request adds latency without changing the fit.
                if (corrected == desired) break;
            }
            var actual = Native.VisibleRect(window.Handle);
            if (CloseRect(actual, desired) && (!window.KeepAspectRatio || (Inside(actual, zone) && HasRatio(window, ratio))))
            {
                window.Result = window.KeepAspectRatio ? "已适配（保持比例）" : "已平铺";
                return true;
            }
            // Some programs enforce an aspect ratio or a fixed size themselves.
            // Try fitting the original content ratio before accepting any result.
            for (int pass = 0; pass < 2; pass++)
            {
                if (!Native.ReadAspect(window, false)) { window.Result = "无法读取窗口内容尺寸"; return false; }
                desired = AspectFit.Fit(zone, ratio, window.FrameWidth, window.FrameHeight);
                if (!await MoveVisibleAsync(window, desired)) return false;
                if (!Native.ReadAspect(window, false)) { window.Result = "无法读取窗口内容尺寸"; return false; }
                if (AspectFit.Fit(zone, ratio, window.FrameWidth, window.FrameHeight) == desired) break;
            }
            actual = Native.VisibleRect(window.Handle);
            if (actual.Width <= zone.Width && actual.Height <= zone.Height && HasRatio(window, ratio))
            {
                // Position only: a fixed-size app may reject another resize even
                // though its accepted dimensions already fit inside the zone.
                var centered = new PixelRect(zone.X + (zone.Width - actual.Width) / 2, zone.Y + (zone.Height - actual.Height) / 2, actual.Width, actual.Height);
                if (!await MoveVisibleAsync(window, centered, false)) return false;
                actual = Native.VisibleRect(window.Handle);
                if (Inside(actual, zone) && CloseRect(actual, centered) && HasRatio(window, ratio))
                {
                    bool automatic = !window.KeepAspectRatio;
                    window.KeepAspectRatio = true;
                    Native.ReadAspect(window, true);
                    window.Result = automatic ? "已适配（自动保持比例）" : "已适配（保持比例，尺寸受限）";
                    return true;
                }
            }
            window.Result = "分区无法容纳窗口或程序拒绝调整，请减少窗口数量或扩大分区";
            return false;
        }
        catch (ArgumentException ex) { window.Result = ex.Message; return false; }
    }

    private static async Task<bool> ConfirmPlacementAsync(WindowItem window, Native.Placement expected)
    {
        // State flags may change before the receiving app finishes its restore
        // animation/layout. Confirm several responsive, stable observations.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        PixelRect? previous = null;
        int stable = 0;
        while (timer.ElapsedMilliseconds < 2500)
        {
            await Task.Delay(50);
            if (!Native.SameWindow(window)) return false;
            var current = Native.Placement.Create();
            if (Native.GetWindowPlacement(window.Handle, ref current) && current.ShowCmd == expected.ShowCmd &&
                current.NormalPosition.Equals(expected.NormalPosition) &&
                (expected.ShowCmd != 2 || (current.Flags & 2) == (expected.Flags & 2)) &&
                Native.SendMessageTimeout(window.Handle, 0, 0, 0, 2, 50, out _) != 0 && Native.GetWindowRect(window.Handle, out var raw))
            {
                stable = raw.Pixels == previous ? stable + 1 : 0;
                previous = raw.Pixels;
                if (stable >= 2) return true;
            }
            else { previous = null; stable = 0; }
        }
        return false;
    }

    private static async Task<bool> RestoreNormalAsync(WindowItem window, Native.Placement original)
    {
        // SW_RESTORE can re-maximize a window minimized from maximized. Explicitly
        // request normal state and clear WPF_RESTORETOMAXIMIZED on this copy only.
        var normal = original;
        normal.ShowCmd = 1; // SW_SHOWNORMAL
        normal.Flags = 4; // WPF_ASYNCWINDOWPLACEMENT
        if (!Native.SetWindowPlacement(window.Handle, in normal))
        {
            window.Result = "无法恢复为普通窗口：" + new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        PixelRect? previous = null;
        int stable = 0;
        while (timer.ElapsedMilliseconds < 2500)
        {
            await Task.Delay(60);
            if (!Native.SameWindow(window)) { window.Result = "窗口已关闭"; return false; }
            // Wait for both the receiving thread and the restore animation to settle.
            bool ready = !Native.IsIconic(window.Handle) && !Native.IsZoomed(window.Handle) &&
                Native.SendMessageTimeout(window.Handle, 0, 0, 0, 2, 50, out _) != 0;
            if (ready && Native.GetWindowRect(window.Handle, out var raw))
            {
                var current = raw.Pixels;
                stable = current == previous ? stable + 1 : 0;
                previous = current;
                if (stable >= 5) { window.State = "已打开"; return true; }
            }
            else { previous = null; stable = 0; }
        }
        window.Result = "恢复普通窗口超时，已跳过平铺；请手动取消最大化后重试";
        return false;
    }

    public async Task<string> UndoAsync()
    {
        if (Busy) return "正在调整窗口，请稍候。";
        if (!CanUndo) return "没有可恢复的位置。";
        Busy = true;
        int restored = 0, missing = 0;
        var failed = new List<(WindowItem Window, Native.Placement Placement)>();
        try
        {
            foreach (var item in undo)
            {
                if (!Native.SameWindow(item.Window)) { missing++; continue; }
                var placement = item.Placement;
                placement.Flags |= 4; // WPF_ASYNCWINDOWPLACEMENT: do not block on a hung application.
                if (Native.SetWindowPlacement(item.Window.Handle, in placement))
                {
                    bool matches = await ConfirmPlacementAsync(item.Window, item.Placement);
                    if (matches) { restored++; item.Window.Result = "已恢复原位置"; item.Window.State = Native.IsIconic(item.Window.Handle) ? "已最小化" : Native.IsZoomed(item.Window.Handle) ? "已最大化" : "已打开"; }
                    else { failed.Add(item); item.Window.Result = "恢复未确认，可重试（显示器可能已改变）"; }
                }
                else { failed.Add(item); item.Window.Result = "恢复失败，可重试"; }
            }
            undo.Clear(); undo.AddRange(failed);
            return $"已恢复 {restored} 个窗口" + (missing > 0 ? $"，{missing} 个窗口已关闭" : "") + (failed.Count > 0 ? $"，{failed.Count} 个待重试。" : "。");
        }
        finally { Busy = false; }
    }
}
