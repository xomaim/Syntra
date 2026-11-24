using System.ComponentModel;

namespace Syntra;

// Changes what the monitor shows: a colour matrix in the Windows compositor,
// the display gamma ramp, and (on NVIDIA) the driver's own Digital Vibrance setting.
static class ColorEngine
{
    static bool magOk;
    static NvVibrance? nv;
    static float[]? lastEffect;
    static ushort[]? lastRamp;
    static int lastNv = -1;
    public static bool HasDriverVibrance => nv != null;
    public static bool HasMatrix => magOk;
    public static string LastError = "";
    static readonly double[] Lum = { 0.2126, 0.7152, 0.0722 };

    public static void Init()
    {
        try { magOk = Native.MagInitialize(); } catch { magOk = false; }
        nv = NvVibrance.TryCreate();
    }

    // Row-vector convention: out = in * M (what the Magnification API uses).
    public static double[,] SatMatrix(double s)
    {
        var m = new double[3, 3];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] = (1 - s) * Lum[i] + (i == j ? s : 0);
        return m;
    }

    public static double[,] HueMatrix(double deg)
    {
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        double[,] k =
        {
            { 0.213 + c * 0.787 - s * 0.213, 0.715 - c * 0.715 - s * 0.715, 0.072 - c * 0.072 + s * 0.928 },
            { 0.213 - c * 0.213 + s * 0.143, 0.715 + c * 0.285 + s * 0.140, 0.072 - c * 0.072 - s * 0.283 },
            { 0.213 - c * 0.213 - s * 0.787, 0.715 - c * 0.715 + s * 0.715, 0.072 + c * 0.928 + s * 0.072 },
        };
        var m = new double[3, 3];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] = k[j, i];
        return m;
    }

    public static double[,] Mul(double[,] a, double[,] b)
    {
        var m = new double[3, 3];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
            m[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
        return m;
    }

    public static float[] Effect(double[,] m, double offset = 0)
    {
        var e = new float[25];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) e[i * 5 + j] = (float)m[i, j];
        e[18] = 1;
        for (int j = 0; j < 3; j++) e[20 + j] = (float)offset;
        e[24] = 1;
        return e;
    }

    // hue/hueSat: whole-screen hue changer (0 / 1 when off).
    public static void Apply(Look l, double lowBlue, double hue = 0, double hueSat = 1, bool forceRamp = false)
    {
        double sat = l.Saturation / 100.0 * hueSat;
        double vib = l.Vibrance;
        if (nv != null)
        {
            int lvl = (int)Math.Round(Math.Max(0, (vib - 50) / 50.0) * 1000);
            if (lvl != lastNv) { nv.Set(lvl / 1000.0); lastNv = lvl; }
            if (vib < 50) sat *= vib / 50.0;
        }
        else sat *= vib >= 50 ? 1 + (vib - 50) / 50.0 * 0.6 : vib / 50.0;

        double temp = Math.Clamp(l.Temperature + lowBlue, -100, 100) / 100.0;
        double[] gains = temp >= 0 ? new[] { 1, 1 - 0.12 * temp, 1 - 0.40 * temp } : new[] { 1 + 0.35 * temp, 1 + 0.10 * temp, 1 };
        double c = l.Contrast / 100.0, b = l.Brightness / 100.0;

        var m = Mul(HueMatrix(hue), SatMatrix(sat));
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] *= gains[j] * c * b;
        var eff = Effect(m, 0.5 * (1 - c));

        if (magOk && (lastEffect == null || !eff.AsSpan().SequenceEqual(lastEffect)))
        {
            try { Native.MagSetFullscreenColorEffect(eff); lastEffect = eff; } catch { }
        }
        var ramp = BuildRamp(l);
        if (forceRamp || lastRamp == null || !ramp.AsSpan().SequenceEqual(lastRamp)) { SetRamp(ramp); lastRamp = ramp; }
    }

    static ushort[] BuildRamp(Look l)
    {
        var r = new ushort[768];
        double g = Math.Clamp(l.Gamma, 0.3, 3.0);
        double sh = l.Shadows / 100.0, hi = l.Highlights / 100.0, bl = l.Blacks / 100.0, wh = l.Whites / 100.0;
        for (int i = 0; i < 256; i++)
        {
            double x = i / 255.0;
            x = Math.Pow(x, 1.0 / g);
            x += sh * 0.30 * x * Math.Pow(1 - x, 2) * 2;
            x += hi * 0.30 * Math.Pow(x, 2) * (1 - x) * 2;
            x = bl * 0.08 + x * (1 - bl * 0.08);
            x *= 1 + wh * 0.10;
            x = Math.Clamp(x, 0, 1);
            ushort v = (ushort)Math.Round(x * 65535);
            r[i] = r[256 + i] = r[512 + i] = v;
        }
        return r;
    }

    static ushort[] LinearRamp()
    {
        var r = new ushort[768];
        for (int i = 0; i < 256; i++) r[i] = r[256 + i] = r[512 + i] = (ushort)(i * 257);
        return r;
    }

    static void SetRamp(ushort[] ramp)
    {
        LastError = "";
        foreach (var s in Screen.AllScreens)
        {
            var dc = Native.CreateDC(null, s.DeviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) continue;
            try { if (!Native.SetDeviceGammaRamp(dc, ramp)) LastError = "Windows limited the gamma range. Try smaller fine-tune values."; }
            finally { Native.DeleteDC(dc); }
        }
    }

    public static void Reset()
    {
        var id = new float[25];
        for (int i = 0; i < 5; i++) id[i * 5 + i] = 1;
        if (magOk) try { Native.MagSetFullscreenColorEffect(id); } catch { }
        SetRamp(LinearRamp());
        nv?.Restore();
        lastEffect = null; lastRamp = null; lastNv = -1;
    }

    public static void Shutdown()
    {
        Reset();
        if (magOk) try { Native.MagUninitialize(); } catch { }
    }
}

// Screen resolution / refresh rate. Changes are "dynamic only": nothing is written system-wide, no admin needed.
static class DisplayModes
{
    public record Mode(int W, int H, int Hz);

    public static Mode Current()
    {
        var dm = New();
        Native.EnumDisplaySettings(null, Native.ENUM_CURRENT_SETTINGS, ref dm);
        return new Mode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
    }

    public static List<Mode> All()
    {
        var list = new List<Mode>();
        var dm = New();
        for (int i = 0; Native.EnumDisplaySettings(null, i, ref dm); i++)
            if (dm.dmBitsPerPel >= 32) list.Add(new Mode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency));
        return list.Distinct().ToList();
    }

    public static bool Apply(Mode m, out string error)
    {
        error = "";
        var dm = New();
        Native.EnumDisplaySettings(null, Native.ENUM_CURRENT_SETTINGS, ref dm);
        dm.dmPelsWidth = m.W; dm.dmPelsHeight = m.H; dm.dmDisplayFrequency = m.Hz;
        dm.dmFields = Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT | Native.DM_DISPLAYFREQUENCY;
        if (Native.ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, Native.CDS_TEST, IntPtr.Zero) != Native.DISP_CHANGE_SUCCESSFUL)
        { error = "This display does not support that mode."; return false; }
        int r = Native.ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
        if (r != Native.DISP_CHANGE_SUCCESSFUL) { error = $"Windows refused the change (code {r})."; return false; }
        return true;
    }

    static Native.DEVMODE New()
    {
        var dm = new Native.DEVMODE { dmDeviceName = "", dmFormName = "" };
        dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<Native.DEVMODE>();
        return dm;
    }
}

// The monitor's own brightness/contrast over DDC/CI — the same as pressing its buttons.
sealed class MonitorDdc : IDisposable
{
    Native.PHYSICAL_MONITOR[] mons = Array.Empty<Native.PHYSICAL_MONITOR>();
    public bool Available => mons.Length > 0;
    public string Name => mons.Length > 0 ? mons[0].szPhysicalMonitorDescription : "No DDC/CI monitor found";
    public uint OrigBrightness = 50, OrigContrast = 50;
    public bool HasBrightness, HasContrast;

    public MonitorDdc()
    {
        try
        {
            var hMon = Native.MonitorFromPoint(new Native.POINT(), 1);
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out var n) || n == 0) return;
            var arr = new Native.PHYSICAL_MONITOR[n];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hMon, n, arr)) return;
            mons = arr;
            HasBrightness = Native.GetMonitorBrightness(mons[0].hPhysicalMonitor, out _, out OrigBrightness, out _);
            HasContrast = Native.GetMonitorContrast(mons[0].hPhysicalMonitor, out _, out OrigContrast, out _);
        }
        catch { mons = Array.Empty<Native.PHYSICAL_MONITOR>(); }
    }

    public void SetBrightness(uint v) { if (HasBrightness) Task.Run(() => { lock (this) Native.SetMonitorBrightness(mons[0].hPhysicalMonitor, v); }); }
    public void SetContrast(uint v) { if (HasContrast) Task.Run(() => { lock (this) Native.SetMonitorContrast(mons[0].hPhysicalMonitor, v); }); }

    public void Dispose()
    {
        if (mons.Length > 0) try { Native.DestroyPhysicalMonitors((uint)mons.Length, mons); } catch { }
    }
}

static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsOn()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("Syntra") != null;
    }

    public static void Set(bool on, string? exe = null)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue("Syntra", $"\"{exe ?? Environment.ProcessPath}\" --tray");
        else k.DeleteValue("Syntra", false);
    }

    public static void RestartAsAdmin()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = true, Verb = "runas", Arguments = "--repair" });
            Program.ExitApp();
        }
        catch (Win32Exception) { /* user said no in the Windows prompt */ }
    }
}
