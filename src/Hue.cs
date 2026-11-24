using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Syntra;

// Per-app hue: a see-through, click-through layer that sits exactly over ONE app's window
// (only while that app is in front) and shows what's underneath with the hue shifted.
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
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int DwmGetInt(IntPtr h, int a, out int v, int size);

    // Out-of-context window events: Windows tells us when windows move or change focus. No injection.
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
    const uint EVENT_SYSTEM_FOREGROUND = 0x3, EVENT_SYSTEM_MOVESIZESTART = 0xA, EVENT_SYSTEM_MOVESIZEEND = 0xB,
               EVENT_SYSTEM_MINIMIZESTART = 0x16, EVENT_SYSTEM_MINIMIZEEND = 0x17,
               EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003, EVENT_OBJECT_LOCATIONCHANGE = 0x800B,
               EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    readonly WinEventProc proc;
    readonly List<IntPtr> hooks = new();
    bool moving;
    RECT lastRect;


    static readonly IntPtr PerMonitorV2 = new(-4);
    readonly System.Windows.Forms.Timer tick = new() { Interval = 16 };
    IntPtr mag;
    IntPtr lastFg;
    string lastName = "";
    bool shown;
    public string Target = "";
    float[] effect = ColorEngine.Effect(ColorEngine.HueMatrix(0));
    public bool Working => mag != IntPtr.Zero;

    public HueOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1, 1);
        var old = SetThreadDpiAwarenessContext(PerMonitorV2);
        try { CreateHandle(); } finally { SetThreadDpiAwarenessContext(old); }
        proc = OnWinEvent;
        hooks.Add(SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0, 2));
        hooks.Add(SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, proc, 0, 0, 2));
        hooks.Add(SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, proc, 0, 0, 2));
        hooks.Add(SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE, IntPtr.Zero, proc, 0, 0, 2));
        hooks.Add(SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, proc, 0, 0, 2));
        hooks.Add(SetWinEventHook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, proc, 0, 0, 2));
        tick.Tick += (_, _) => Follow();
        tick.Start();
    }

    void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (ev == EVENT_SYSTEM_FOREGROUND) { moving = false; Follow(); return; }
        if (hwnd != lastFg || idObject != 0) return; // only the window we're covering
        switch (ev)
        {
            case EVENT_SYSTEM_MOVESIZESTART: moving = true; Hide2(); break;   // hide while dragging: no trailing/lag
            case EVENT_SYSTEM_MOVESIZEEND: moving = false; Follow(); break;
            default: Follow(); break;                                         // snapped, maximised, minimised, closed, hidden
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000 | 0x8; // layered, click-through, tool, no-activate, topmost
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

    void Hide2() { if (shown) { ShowWindow(Handle, 0); shown = false; lastRect = default; } }

    void Follow()
    {
        if (mag == IntPtr.Zero || Target == "") { Hide2(); return; }
        var fg = Native.GetForegroundWindow();
        if (fg != lastFg)
        {
            lastFg = fg;
            lastName = "";
            try
            {
                Native.GetWindowThreadProcessId(fg, out var pid);
                using var p = Process.GetProcessById((int)pid);
                lastName = p.ProcessName;
            }
            catch { }
        }
        if (fg == IntPtr.Zero || moving || !string.Equals(lastName, Target, StringComparison.OrdinalIgnoreCase)
            || !IsWindow(fg) || !IsWindowVisible(fg) || IsIconic(fg) || Cloaked(fg)) { Hide2(); return; }
        if (!IsWindow(mag)) MakeMag();

        var old = SetThreadDpiAwarenessContext(PerMonitorV2);
        try
        {
            if (DwmGetWindowAttribute(fg, 9, out var r, Marshal.SizeOf<RECT>()) != 0) GetWindowRect(fg, out r);
            int w = r.R - r.L, h = r.B - r.T;
            if (w < 2 || h < 2) { Hide2(); return; }
            if (!shown || r.L != lastRect.L || r.T != lastRect.T || r.R != lastRect.R || r.B != lastRect.B)
            {
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.L, r.T, w, h, 0x0010 | 0x0040); // no-activate, show
                MoveWindow(mag, 0, 0, w, h, false);
                lastRect = r;
                shown = true;
            }
            MagSetWindowSource(mag, r);
            InvalidateRect(mag, IntPtr.Zero, true);
        }
        finally { SetThreadDpiAwarenessContext(old); }
    }

    static bool Cloaked(IntPtr h) => DwmGetInt(h, 14, out var c, 4) == 0 && c != 0;

    protected override void Dispose(bool disposing)
    {
        foreach (var hk in hooks) if (hk != IntPtr.Zero) UnhookWinEvent(hk);
        hooks.Clear();
        if (disposing) tick.Dispose();
        base.Dispose(disposing);
    }
}
