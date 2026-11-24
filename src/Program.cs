using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;
using Drawing = System.Drawing;

namespace Dusk;

public class Settings
{
    public bool Enabled { get; set; } = true;
    public double Kelvin { get; set; } = 3400;
    public double Brightness { get; set; } = 1;
    public bool FlickerFree { get; set; } = true;
    public bool Sounds { get; set; } = true;
    public bool ScheduleOn { get; set; }
    public int StartHour { get; set; } = 20;
    public int EndHour { get; set; } = 7;
    public SavedBacklight? Pinned { get; set; } // non-null while backlights are held at 100%

    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dusk");
    static readonly string File_ = Path.Combine(Dir, "settings.json");
    string last = "";

    public static Settings Load()
    {
        Settings s;
        var fresh = !File.Exists(File_);
        try { s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(File_)) ?? new(); }
        catch { s = new(); }
        // First run: flicker-free is on, so start the software dim at the current laptop backlight, not brighter.
        if (fresh && Backlight.LaptopGet() is byte b) s.Brightness = b / 100.0;
        // A hand-edited or corrupt file must not crash the UI or black out the screen.
        s.Kelvin = Math.Clamp(s.Kelvin, 500, 6500);
        s.Brightness = Math.Clamp(s.Brightness, 0.05, 1);
        s.StartHour = Math.Clamp(s.StartHour, 0, 23);
        s.EndHour = Math.Clamp(s.EndHour, 0, 23);
        s.last = fresh ? "" : JsonSerializer.Serialize(s); // fresh: save once so the seed isn't re-read from a pinned backlight
        return s;
    }

    /// Writes only when something changed, via temp file + rename so a crash can't leave half a file.
    public void Save()
    {
        var json = JsonSerializer.Serialize(this);
        if (json == last) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(File_ + ".tmp", json);
            File.Move(File_ + ".tmp", File_, true);
            last = json;
        }
        catch (IOException) { } // locked by AV or sync tools; next change retries
        catch (UnauthorizedAccessException) { }
    }
}

static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool On
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("Dusk") is not null;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("Dusk", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("Dusk", false);
        }
    }
}

static class Program
{
    public static readonly (string Name, int Kelvin)[] Presets =
    {
        ("Energy", 6000), ("Daylight", 5000), ("Evening", 3400), ("Night", 2700),
        ("Late", 1900), ("Deep", 1200), ("Midnight", 500),
    };

    public static Settings S = Settings.Load();
    public static bool GammaOK = true;
    static bool gammaTouched; // only reset gamma if Dusk changed it, so calibration LUTs from other tools survive
    static NotifyIcon tray = null!;
    static DuskWindow panel = null!;
    static bool? trayOn;

    [STAThread]
    static void Main(string[] args)
    {
        using var single = new Mutex(true, "Dusk.SingleInstance", out var first);
        if (!first) return;

        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Log(e.Exception); e.Handled = true; };
        Theme.LoadFonts();

        // Last run crashed or was killed while pinned: put the user's real levels back first.
        if (S.Pinned is { } left) { Backlight.Restore(left); S.Pinned = null; S.Save(); }

        panel = new DuskWindow();
        tray = new NotifyIcon { Visible = true, ContextMenuStrip = BuildMenu() };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) panel.Toggle(); };

        var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        tick.Tick += (_, _) => Apply();
        tick.Start();

        // Windows resets gamma on display events, monitors can lose DDC state on sleep,
        // and laptops switch to a separate AC/DC brightness when the charger changes.
        var later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        later.Tick += (_, _) => { later.Stop(); Apply(repin: true); };
        void Soon() => app.Dispatcher.BeginInvoke(() => { later.Stop(); later.Start(); });
        SystemEvents.DisplaySettingsChanged += (_, _) => Soon();
        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode is PowerModes.Resume or PowerModes.StatusChange) Soon(); };
        SystemEvents.SessionSwitch += (_, e) => { if (e.Reason == SessionSwitchReason.SessionUnlock) Soon(); };
        SystemEvents.UserPreferenceChanged += (_, _) => app.Dispatcher.BeginInvoke(() => { trayOn = null; UpdateTray(); }); // taskbar light/dark
        // SessionEnding can still be cancelled; SessionEnded can't.
        SystemEvents.SessionEnded += (_, _) => Restore();
        app.Exit += (_, _) => { Restore(); tray.Visible = false; };

        Apply();
        if (args.Contains("--open") || args.Contains("--settings")) panel.Toggle(); // lets CI screenshot the panel
        if (args.Contains("--settings")) panel.ShowSettingsPage();
        app.Run();
    }

    static bool InNight()
    {
        var h = DateTime.Now.Hour;
        return S.StartHour <= S.EndHour ? h >= S.StartHour && h < S.EndHour : h >= S.StartHour || h < S.EndHour;
    }

    static (double K, double Dim) shown = (6500, 1); // what the gamma ramp shows right now
    static bool wasActive;
    static System.Windows.Threading.DispatcherTimer? anim;

    public static void Apply(bool repin = false)
    {
        var active = S.Enabled && (!S.ScheduleOn || InNight());
        var animate = active != wasActive || anim is not null;
        wasActive = active;
        anim?.Stop(); anim = null;
        var pin = S.FlickerFree && active;

        // Flicker-free: hand the laptop backlight's level over to gamma first, so pinning to 100% looks the same.
        if (pin && S.Pinned is null && Backlight.LaptopGet() is byte b && Gamma.Apply(shown.K, shown.Dim * b / 100.0))
        {
            shown.Dim *= b / 100.0;
            S.Pinned = new();
            S.Save(); // persist intent before touching hardware
            Backlight.Pin(S.Pinned);
            gammaTouched = true;
        }
        var unpin = !pin && S.Pinned is not null;
        var target = active ? (K: S.Kelvin, Dim: S.Brightness) : (K: 6500.0, Dim: unpin && S.Pinned!.Laptop is byte l ? l / 100.0 : 1);

        void Finish()
        {
            if (active || unpin) { GammaOK = Gamma.Apply(target.K, target.Dim); gammaTouched = true; }
            shown = target;
            // Pin only if gamma verifiably applied, else pinning would make the screen brighter.
            if (pin && GammaOK) { if (S.Pinned is null || repin) { S.Pinned ??= new(); S.Save(); Backlight.Pin(S.Pinned); } }
            else if (S.Pinned is { } p) { Backlight.Restore(p); S.Pinned = null; if (active) GammaOK = Gamma.Apply(target.K, target.Dim); }
            if (!active) { if (gammaTouched) Gamma.Reset(); gammaTouched = false; GammaOK = true; shown = (6500, 1); }
            UpdateTray();
            panel.Sync();
            S.Save();
        }
        if (!animate) { Finish(); return; }

        // Sunset on, sunrise off: ease warmth (in mireds, so it looks even) and dim over 1.6s.
        var from = shown;
        var start = DateTime.UtcNow;
        anim = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        anim.Tick += (_, _) =>
        {
            var x = Math.Min((DateTime.UtcNow - start).TotalSeconds / 1.6, 1);
            if (x >= 1) { anim?.Stop(); anim = null; Finish(); return; }
            var e = x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
            var m = 1e6 / from.K + (1e6 / target.K - 1e6 / from.K) * e;
            shown = (1e6 / m, from.Dim + (target.Dim - from.Dim) * e);
            Gamma.Set(shown.K, shown.Dim);
            gammaTouched = true;
        };
        anim.Start();
        UpdateTray();
        panel.Sync();
    }

    static void Restore()
    {
        anim?.Stop(); anim = null;
        if (S.Pinned is { } p) { Backlight.Restore(p); S.Pinned = null; S.Save(); }
        if (gammaTouched) Gamma.Reset();
        gammaTouched = false;
    }

    static void UpdateTray()
    {
        tray.Text = !S.Enabled ? "Dusk: off" : $"Dusk: {S.Kelvin:0}K, {S.Brightness * 100:0}%";
        if (trayOn == S.Enabled) return;
        trayOn = S.Enabled;
        var old = tray.Icon;
        tray.Icon = TrayIcon(S.Enabled);
        old?.Dispose();
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    /// Slit-sun mark, filled when on and outlined when off, like the Mac menu-bar icon.
    /// White on a dark taskbar, black on a light one.
    static Drawing.Icon TrayIcon(bool on)
    {
        var size = SystemInformation.SmallIconSize.Width;
        using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var light = personalize?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        var ink = light ? Drawing.Color.Black : Drawing.Color.White;
        using var bmp = new Drawing.Bitmap(size, size);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            var s = size / 18f;
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var disc = new Drawing.RectangleF(1.5f * s, 1.5f * s, 15 * s, 15 * s);
            if (on) { using var b = new Drawing.SolidBrush(ink); g.FillEllipse(b, disc); }
            else { using var pen = new Drawing.Pen(ink, Math.Max(1.5f * s, 1)); g.DrawEllipse(pen, disc); }
            g.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy;
            using var clear = new Drawing.SolidBrush(Drawing.Color.Transparent);
            foreach (var (y, h) in new[] { (9.5f, 1.1f), (11.6f, 1.5f), (13.7f, 1.9f) })
                g.FillRectangle(clear, 0, y * s, size, Math.Max(h * s, 1));
        }
        var hIcon = bmp.GetHicon();
        var icon = (Drawing.Icon)Drawing.Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    static void Log(Exception e)
    {
        try { Directory.CreateDirectory(Settings.Dir); File.AppendAllText(Path.Combine(Settings.Dir, "error.log"), $"{DateTime.Now:s} {e}\n"); }
        catch { }
    }

    static ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip();
        m.Opening += (_, _) =>
        {
            m.Items.Clear();
            m.Items.Add("Open Dusk", null, (_, _) => panel.Toggle());
            m.Items.Add(new ToolStripMenuItem("On", null, (_, _) => { S.Enabled = !S.Enabled; Apply(); }) { Checked = S.Enabled });
            m.Items.Add(new ToolStripSeparator());
            foreach (var (name, k) in Presets)
                m.Items.Add(new ToolStripMenuItem($"{name}  {k}K", null, (_, _) => { S.Kelvin = k; S.Enabled = true; Apply(); }) { Checked = S.Kelvin == k });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Quit Dusk", null, (_, _) => System.Windows.Application.Current.Shutdown());
        };
        m.Items.Add("…"); // Opening only fires when the strip has items
        return m;
    }
}
