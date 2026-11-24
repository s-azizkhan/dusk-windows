using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Point = System.Drawing.Point;

// Load Win32 DLLs only from System32, never from the exe's folder (e.g. Downloads).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Dusk;

public static class Gamma
{
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateDC(string driver, string? device, string? output, IntPtr devMode);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);
    [DllImport("gdi32.dll")] static extern bool GetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);

    const string IcmKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";

    /// How much of the last Apply stuck, 0 (nothing) to 1 (all), on the worst display.
    public static double Reached { get; private set; } = 1;

    /// Sets warmth + dim on every display. False if any display didn't take it fully (Windows fails silently, so read back).
    /// When a driver rejects the ramp, falls back to the warmest blend toward neutral it accepts.
    public static bool Apply(double kelvin, double dim, double? laptopDim = null, string? laptop = null)
    {
        var reached = 1.0;
        foreach (var s in Screen.AllScreens)
        {
            var want = Warmth.Ramp(kelvin, laptopDim is double ld && s.DeviceName == laptop ? ld : dim);
            var dc = CreateDC(s.DeviceName, null, null, IntPtr.Zero); // documented form: driver = display device name
            if (dc == IntPtr.Zero) { reached = 0; continue; }
            if (TrySet(dc, want)) cap.Remove(s.DeviceName);
            else if (cap.TryGetValue(s.DeviceName, out var c) && c.Want.SequenceEqual(want) && TrySet(dc, Warmth.Blend(want, c.T)))
                reached = Math.Min(reached, c.T); // same target as last time: reuse the cap, no visible search every tick
            else
            {
                double lo = 0, hi = 1; // ponytail: 7 halvings, under 1% of the range
                for (var i = 0; i < 7; i++)
                {
                    var mid = (lo + hi) / 2;
                    if (TrySet(dc, Warmth.Blend(want, mid))) lo = mid; else hi = mid;
                }
                TrySet(dc, Warmth.Blend(want, lo));
                cap[s.DeviceName] = (want, lo);
                reached = Math.Min(reached, lo);
            }
            DeleteDC(dc);
        }
        Reached = reached;
        return reached >= 1;
    }

    /// One animation frame: no read-back or fallback, a rejected frame just doesn't show.
    public static void Set(double kelvin, double dim, double? laptopDim = null, string? laptop = null)
    {
        foreach (var s in Screen.AllScreens)
        {
            var ramp = Warmth.Ramp(kelvin, laptopDim is double ld && s.DeviceName == laptop ? ld : dim);
            var dc = CreateDC(s.DeviceName, null, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) continue;
            SetDeviceGammaRamp(dc, ramp);
            DeleteDC(dc);
        }
    }

    static readonly Dictionary<string, (ushort[] Want, double T)> cap = new();

    static bool TrySet(IntPtr dc, ushort[] ramp)
    {
        var got = new ushort[768];
        return SetDeviceGammaRamp(dc, ramp) && GetDeviceGammaRamp(dc, got) && Warmth.Matches(ramp, got);
    }

    public static void Reset() => Apply(6500, 1);

    // Without this value Windows refuses ramps far from identity (warm or dim settings).
    public static bool Unlocked
    {
        get
        {
            using var k = Registry.LocalMachine.OpenSubKey(IcmKey);
            return k?.GetValue("GdiIcmGammaRange") is int v && v == 256;
        }
    }

    /// One UAC prompt to set GdiIcmGammaRange = 256 (same value LightBulb's installer sets).
    public static bool Unlock()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("reg.exe",
                $"add \"HKLM\\{IcmKey}\" /v GdiIcmGammaRange /t REG_DWORD /d 256 /f")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }
        catch (Win32Exception) { return false; } // user said no to UAC
    }
}

/// Backlight levels from before pinning. Persisted so a crash or forced reboot can still be undone on next launch.
public class SavedBacklight
{
    public byte? Laptop { get; set; }
    public Dictionary<string, uint> External { get; set; } = new();
}

/// Flicker-free: hold backlights at 100% and dim in gamma instead.
public static class Backlight
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    struct PhysicalMonitor { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Desc; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice dd, uint flags);
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint count);
    [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint count, [Out] PhysicalMonitor[] mons);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] mons);
    [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr h, uint value);

    /// Pins everything to 100%, recording original levels into `saved` (first value wins, so repins keep the real original).
    public static void Pin(SavedBacklight saved)
    {
        var cur = LaptopGet();
        if (cur is byte b)
        {
            saved.Laptop ??= b;
            if (b != 100) LaptopSet(100);
        }
        EachExternal((key, h) =>
        {
            if (!GetMonitorBrightness(h, out _, out var c, out var max)) return;
            saved.External.TryAdd(key, c);
            if (c != max) SetMonitorBrightness(h, max);
        });
    }

    public static void Restore(SavedBacklight saved)
    {
        if (saved.Laptop is byte b) LaptopSet(b);
        EachExternal((key, h) => { if (saved.External.TryGetValue(key, out var v)) SetMonitorBrightness(h, v); });
    }

    /// Current and max level of every DDC/CI monitor that answers, for the self-test.
    public static List<(uint Cur, uint Max)> ExternalLevels()
    {
        var l = new List<(uint, uint)>();
        EachExternal((_, h) => { if (GetMonitorBrightness(h, out var _min, out var c, out var m)) l.Add((c, m)); });
        return l;
    }

    // DDC/CI. Keyed by the monitor's hardware ID (MONITOR\GSM5B7F\{...}\0001) so restores survive re-enumeration.
    // Monitors without DDC/CI just fail GetMonitorBrightness and are skipped.
    static void EachExternal(Action<string, IntPtr> act)
    {
        foreach (var s in Screen.AllScreens)
        {
            var c = s.Bounds;
            var hmon = MonitorFromPoint(new(c.Left + c.Width / 2, c.Top + c.Height / 2), 0 /* MONITOR_DEFAULTTONULL */);
            if (hmon == IntPtr.Zero || !GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out var n) || n == 0) continue;
            var mons = new PhysicalMonitor[n];
            if (!GetPhysicalMonitorsFromHMONITOR(hmon, n, mons)) continue;
            for (var i = 0u; i < n; i++)
            {
                var dd = new DisplayDevice { cb = (uint)Marshal.SizeOf<DisplayDevice>() };
                var id = EnumDisplayDevices(s.DeviceName, i, ref dd, 0) ? dd.DeviceID : $"{s.DeviceName}/{i}";
                act($"{id}|{mons[i].Desc}", mons[i].Handle);
            }
            DestroyPhysicalMonitors(n, mons);
        }
    }

    /// The screen (\\.\DISPLAYn) that is the laptop panel: its monitor ID matches the WMI brightness instance.
    public static string? LaptopScreen()
    {
        try
        {
            using var q = new ManagementObjectSearcher(new ManagementScope(@"root\wmi"), new SelectQuery("WmiMonitorBrightness"));
            foreach (ManagementObject o in q.Get())
            {
                var hw = ((string)o["InstanceName"]).Split('\\').ElementAtOrDefault(1); // DISPLAY\BOE0867\... -> BOE0867
                if (string.IsNullOrEmpty(hw)) return null;
                foreach (var s in Screen.AllScreens)
                {
                    var dd = new DisplayDevice { cb = (uint)Marshal.SizeOf<DisplayDevice>() };
                    if (EnumDisplayDevices(s.DeviceName, 0, ref dd, 0) && dd.DeviceID.Contains(hw, StringComparison.OrdinalIgnoreCase)) return s.DeviceName;
                }
            }
        }
        catch (Exception) { } // no laptop panel or WMI off
        return null;
    }

    static ManagementEventWatcher? watcher;

    /// Pushes laptop brightness changes (keys, Action Center, auto-brightness) instead of polling WMI.
    public static void WatchLaptop(Action<byte> changed)
    {
        try
        {
            watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            watcher.EventArrived += (_, e) => { if (e.NewEvent["Brightness"] is byte b) changed(b); };
            watcher.Start();
        }
        catch (Exception) { watcher = null; } // desktops and WMI-less systems: nothing to follow
    }

    // Built-in panel via WMI. Desktops have no instance and throw ManagementException; treat as "no laptop panel".
    public static byte? LaptopGet()
    {
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"root\wmi"), new SelectQuery("WmiMonitorBrightness"));
            foreach (ManagementObject o in s.Get()) return (byte)o["CurrentBrightness"];
        }
        catch (Exception) { } // optional hardware path: missing class, WMI off, or wminet_utils.dll load failure
        return null;
    }

    public static void LaptopSet(byte value)
    {
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"root\wmi"), new SelectQuery("WmiMonitorBrightnessMethods"));
            foreach (ManagementObject o in s.Get()) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, value });
        }
        catch (Exception) { } // optional hardware path: missing class, WMI off, or wminet_utils.dll load failure
    }
}
