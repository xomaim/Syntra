using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Syntra;

// Per-app hue: a see-through, click-through layer that sits directly above ONE app's window
// (wherever it is — any monitor, focused or not) and shows what's underneath with the hue shifted.
// It uses Windows' Magnifier control to copy the screen. It never opens, reads or injects into the app.
sealed class HueOverlay : Form
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("Magnification.dll")] static extern bool MagSetWindowSource(IntPtr h, RECT r);
    [DllImport("Magnification.dll")] static extern bool MagSetColorEffect(IntPtr h, float[] e);
    [DllImport("Magnification.dll")] static extern bool MagSetWindowFilterList(IntPtr h, int mode, int count, IntPtr[] list);
    [DllImport("Magnification.dll")] static extern bool MagSetWindowTransform(IntPtr h, float[] m);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int DwmGetInt(IntPtr h, int a, out int v, int size);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);

    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
    const uint EVENT_SYSTEM_FOREGROUND = 0x3, EVENT_SYSTEM_MOVESIZESTART = 0xA, EVENT_SYSTEM_MOVESIZEEND = 0xB,
               EVENT_SYSTEM_MINIMIZESTART = 0x16, EVENT_SYSTEM_MINIMIZEEND = 0x17,
               EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_HIDE = 0x8003, EVENT_OBJECT_REORDER = 0x8004,
               EVENT_OBJECT_LOCATIONCHANGE = 0x800B, EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    const uint GW_HWNDPREV = 3, GW_OWNER = 4;

    static readonly IntPtr PerMonitorV2 = new(-4);
    readonly System.Windows.Forms.Timer tick = new() { Interval = 16 };
    readonly WinEventProc proc;
    readonly List<IntPtr> hooks = new();
    readonly Dictionary<uint, string> names = new();
    IntPtr mag, target;
    DateTime lastScan;
    bool shown, moving;
    RECT lastRect;
    string targetName = "";
    float[] effect = ColorEngine.Effect(ColorEngine.HueMatrix(0));

    public string Target
    {
        get => targetName;
        set { if (!string.Equals(value, targetName, StringComparison.OrdinalIgnoreCase)) { targetName = value; target = IntPtr.Zero; lastScan = default; } }
    }

    public HueOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1, 1);
        var old = SetThreadDpiAwarenessContext(PerMonitorV2);
        try { CreateHandle(); } finally { SetThreadDpiAwarenessContext(old); }

        proc = OnWinEvent;
        void Hook(uint a, uint b) => hooks.Add(SetWinEventHook(a, b, IntPtr.Zero, proc, 0, 0, 2)); // out-of-context, skip own process
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        Hook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND);
        Hook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND);
        Hook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_REORDER);
        Hook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE);
        Hook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED);
        tick.Tick += (_, _) => Follow();
        tick.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000; // layered, click-through, tool window, never takes focus
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetLayeredWindowAttributes(Handle, 0, 255, 2);
        MakeMag();
    }

    void MakeMag()
    {
        if (mag != IntPtr.Zero) { try { DestroyWindow(mag); } catch { } mag = IntPtr.Zero; }
        mag = CreateWindowEx(0, "Magnifier", "SyntraHue", 0x40000000 | 0x10000000, 0, 0, 1, 1, Handle, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (mag == IntPtr.Zero) return;
        MagSetWindowTransform(mag, new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 });
        MagSetWindowFilterList(mag, 0, 1, new[] { Handle });
        MagSetColorEffect(mag, effect);
    }

    public void SetEffect(double hue, double sat)
    {
        effect = ColorEngine.Effect(ColorEngine.Mul(ColorEngine.HueMatrix(hue), ColorEngine.SatMatrix(sat)));
        if (mag != IntPtr.Zero) MagSetColorEffect(mag, effect);
    }

    void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (ev == EVENT_SYSTEM_FOREGROUND) { Follow(); return; }   // something came to the front: restack above the app
        if (hwnd != target || target == IntPtr.Zero || idObject != 0) return;
        switch (ev)
        {
            case EVENT_SYSTEM_MOVESIZESTART: moving = true; Hide2(); break;   // hide while dragging: no trailing/lag
            case EVENT_SYSTEM_MOVESIZEEND: moving = false; Follow(); break;
            case EVENT_OBJECT_DESTROY: target = IntPtr.Zero; Hide2(); break;
            default: Follow(); break;
        }
    }

    void Hide2() { if (shown) { ShowWindow(Handle, 0); shown = false; lastRect = default; } }

    string NameOf(uint pid)
    {
        if (names.TryGetValue(pid, out var n)) return n;
        try { using var p = Process.GetProcessById((int)pid); n = p.ProcessName; } catch { n = ""; }
        if (names.Count > 500) names.Clear();
        return names[pid] = n;
    }

    static bool Cloaked(IntPtr h) => DwmGetInt(h, 14, out var c, 4) == 0 && c != 0;

    bool IsTargetWindow(IntPtr h)
    {
        if (!IsWindow(h)) return false;
        Native.GetWindowThreadProcessId(h, out var pid);
        return string.Equals(NameOf(pid), targetName, StringComparison.OrdinalIgnoreCase);
    }

    // The app's main window: its biggest visible, titled, top-level window.
    IntPtr FindTarget()
    {
        if (target != IntPtr.Zero && IsTargetWindow(target)) return target;
        target = IntPtr.Zero;
        if (DateTime.UtcNow - lastScan < TimeSpan.FromSeconds(1)) return IntPtr.Zero;
        lastScan = DateTime.UtcNow;
        IntPtr best = IntPtr.Zero; long bestArea = 0;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero || GetWindowTextLength(h) == 0) return true;
            if ((GetWindowLong(h, -20) & 0x80) != 0 || Cloaked(h)) return true; // tool windows / hidden
            if (!IsTargetWindow(h)) return true;
            GetWindowRect(h, out var r);
            long area = (long)(r.R - r.L) * (r.B - r.T);
            if (area > bestArea) { bestArea = area; best = h; }
            return true;
        }, IntPtr.Zero);
        return target = best;
    }

    void Follow()
    {
        if (mag == IntPtr.Zero || targetName == "") { Hide2(); return; }
        var t = FindTarget();
        if (t == IntPtr.Zero || moving || !IsWindowVisible(t) || IsIconic(t) || Cloaked(t)) { Hide2(); return; }
        if (!IsWindow(mag)) MakeMag();

        var old = SetThreadDpiAwarenessContext(PerMonitorV2);
        try
        {
            if (DwmGetWindowAttribute(t, 9, out var r, Marshal.SizeOf<RECT>()) != 0) GetWindowRect(t, out r);
            int w = r.R - r.L, h = r.B - r.T;
            if (w < 2 || h < 2) { Hide2(); return; }

            // Sit directly above the app in the window stack — not on top of everything.
            // Windows you put over the app still cover it normally.
            var above = GetWindow(t, GW_HWNDPREV);
            bool moved = r.L != lastRect.L || r.T != lastRect.T || r.R != lastRect.R || r.B != lastRect.B;
            if (!shown || moved || above != Handle)
            {
                Native.SetWindowPos(Handle, above == Handle ? Handle : above, r.L, r.T, w, h,
                    0x0010u | 0x0040u | (above == Handle ? 0x0004u : 0u)); // no-activate, show, (keep z if already right)
                if (moved || !shown) MoveWindow(mag, 0, 0, w, h, false);
                lastRect = r;
                shown = true;
            }
            MagSetWindowSource(mag, r);
            InvalidateRect(mag, IntPtr.Zero, true);
        }
        finally { SetThreadDpiAwarenessContext(old); }
    }

    protected override void Dispose(bool disposing)
    {
        foreach (var hk in hooks) if (hk != IntPtr.Zero) UnhookWinEvent(hk);
        hooks.Clear();
        if (disposing) tick.Dispose();
        base.Dispose(disposing);
    }
}
