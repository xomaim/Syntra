using System.Runtime.InteropServices;

namespace Syntra;

// Only plain user-mode Windows APIs. Nothing here opens, reads or writes another process.
static class Native
{
    // ---- Gamma ramp (gdi32) ----
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateDC(string? driver, string device, string? output, IntPtr init);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool SetDeviceGammaRamp(IntPtr dc, ushort[] ramp);

    // ---- Magnification API: full-screen colour matrix, done by Windows' own compositor ----
    [DllImport("Magnification.dll")] public static extern bool MagInitialize();
    [DllImport("Magnification.dll")] public static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] public static extern bool MagSetFullscreenColorEffect(float[] effect);

    // ---- Display modes ----
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    public const int CDS_TEST = 0x2, DISP_CHANGE_SUCCESSFUL = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY;
        public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(string? device, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);

    // ---- Monitor DDC/CI (dxva2) ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint count);
    [DllImport("dxva2.dll")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint count, [Out] PHYSICAL_MONITOR[] mons);
    [DllImport("dxva2.dll")] public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] mons);
    [DllImport("dxva2.dll")] public static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
    [DllImport("dxva2.dll")] public static extern bool SetMonitorBrightness(IntPtr h, uint v);
    [DllImport("dxva2.dll")] public static extern bool GetMonitorContrast(IntPtr h, out uint min, out uint cur, out uint max);
    [DllImport("dxva2.dll")] public static extern bool SetMonitorContrast(IntPtr h, uint v);

    // ---- Windows / hotkeys ----
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const int WM_HOTKEY = 0x0312;
}

// NVIDIA "Digital Vibrance" through nvapi64.dll, which ships with the NVIDIA driver you already have.
// This is the same setting as in NVIDIA Control Panel. Nothing is installed.
sealed class NvVibrance
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitD();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EnumD(int i, out IntPtr h);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetInfoD(IntPtr h, int output, ref DVC info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetD(IntPtr h, int output, int level);

    [StructLayout(LayoutKind.Sequential)]
    struct DVC { public uint version; public int cur, min, max; }

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr QueryInterface(uint id);

    readonly List<(IntPtr h, int orig, int max)> displays = new();
    SetD? set;

    static T Get<T>(uint id) where T : Delegate
    {
        var p = QueryInterface(id);
        if (p == IntPtr.Zero) throw new EntryPointNotFoundException();
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    public static NvVibrance? TryCreate()
    {
        try
        {
            if (Get<InitD>(0x0150E828)() != 0) return null;
            var en = Get<EnumD>(0x9ABDD40D);
            var gi = Get<GetInfoD>(0x4085DE45);
            var nv = new NvVibrance { set = Get<SetD>(0x172409B4) };
            for (int i = 0; i < 8; i++)
            {
                if (en(i, out var h) != 0) break;
                var d = new DVC { version = (uint)(Marshal.SizeOf<DVC>() | (1 << 16)) };
                if (gi(h, 0, ref d) == 0 && d.max > 0) nv.displays.Add((h, d.cur, d.max));
            }
            return nv.displays.Count > 0 ? nv : null;
        }
        catch { return null; }
    }

    public void Set(double fraction)
    {
        foreach (var d in displays)
            try { set!(d.h, 0, (int)Math.Round(Math.Clamp(fraction, 0, 1) * d.max)); } catch { }
    }

    public void Restore()
    {
        foreach (var d in displays) try { set!(d.h, 0, d.orig); } catch { }
    }
}
