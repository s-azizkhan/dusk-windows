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

    /// Sets warmth + dim on every display. False if any display didn't take it (Windows fails silently, so read back).
    public static bool Apply(double kelvin, double dim)
    {
        var want = Warmth.Ramp(kelvin, dim);
        var ok = true;
        foreach (var s in Screen.AllScreens)
        {
            var dc = CreateDC(s.DeviceName, null, null, IntPtr.Zero); // documented form: driver = display device name
            if (dc == IntPtr.Zero) { ok = false; continue; }
            var got = new ushort[768];
            if (!SetDeviceGammaRamp(dc, want) || !GetDeviceGammaRamp(dc, got) || !Warmth.Matches(want, got)) ok = false;
            DeleteDC(dc);
        }
        return ok;
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

    // Built-in panel via WMI. Desktops have no instance and throw ManagementException; treat as "no laptop panel".
    static byte? LaptopGet()
    {
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"root\wmi"), new SelectQuery("WmiMonitorBrightness"));
            foreach (ManagementObject o in s.Get()) return (byte)o["CurrentBrightness"];
        }
        catch (Exception) { } // optional hardware path: missing class, WMI off, or wminet_utils.dll load failure
        return null;
    }

    static void LaptopSet(byte value)
    {
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"root\wmi"), new SelectQuery("WmiMonitorBrightnessMethods"));
            foreach (ManagementObject o in s.Get()) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, value });
        }
        catch (Exception) { } // optional hardware path: missing class, WMI off, or wminet_utils.dll load failure
    }
}
