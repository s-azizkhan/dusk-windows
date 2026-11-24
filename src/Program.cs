using System.Text.Json;
using Microsoft.Win32;
using Drawing = System.Drawing;

namespace Dusk;

public class Settings
{
    public bool Enabled { get; set; } = true;
    public double Kelvin { get; set; } = 3400;
    public double Brightness { get; set; } = 1;
    public bool FlickerFree { get; set; }
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
        try { s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(File_)) ?? new(); }
        catch { s = new(); }
        // A hand-edited or corrupt file must not crash the UI or black out the screen.
        s.Kelvin = Math.Clamp(s.Kelvin, 500, 6500);
        s.Brightness = Math.Clamp(s.Brightness, 0.05, 1);
        s.StartHour = Math.Clamp(s.StartHour, 0, 23);
        s.EndHour = Math.Clamp(s.EndHour, 0, 23);
        s.last = JsonSerializer.Serialize(s);
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
    static Popup popup = null!;

    [STAThread]
    static void Main(string[] args)
    {
        using var single = new Mutex(true, "Dusk.SingleInstance", out var first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Log(e.Exception);

        // Last run crashed or was killed while pinned: put the user's real levels back first.
        if (S.Pinned is { } left) { Backlight.Restore(left); S.Pinned = null; S.Save(); }

        popup = new Popup();
        using var icon = typeof(Program).Assembly.GetManifestResourceStream("Dusk.ico")!;
        tray = new NotifyIcon
        {
            Icon = new Drawing.Icon(icon, SystemInformation.SmallIconSize), // 16/20/24px frame, not a scaled-down 32px one
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) popup.Toggle(); };

        var tick = new System.Windows.Forms.Timer { Interval = 30_000 };
        tick.Tick += (_, _) => Apply();
        tick.Start();

        // Windows resets gamma on display events, monitors can lose DDC state on sleep,
        // and laptops switch to a separate AC/DC brightness when the charger changes.
        var later = new System.Windows.Forms.Timer { Interval = 2000 };
        later.Tick += (_, _) => { later.Stop(); Apply(repin: true); };
        void Soon() { later.Stop(); later.Start(); }
        SystemEvents.DisplaySettingsChanged += (_, _) => Soon();
        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode is PowerModes.Resume or PowerModes.StatusChange) Soon(); };
        SystemEvents.SessionSwitch += (_, e) => { if (e.Reason == SessionSwitchReason.SessionUnlock) Soon(); };
        // SessionEnding can still be cancelled; SessionEnded can't.
        SystemEvents.SessionEnded += (_, _) => Restore();
        Application.ApplicationExit += (_, _) => { Restore(); tray.Visible = false; };

        Apply();
        if (args.Contains("--open")) popup.Toggle(); // lets CI screenshot the panel
        Application.Run();
    }

    static bool InNight()
    {
        var h = DateTime.Now.Hour;
        return S.StartHour <= S.EndHour ? h >= S.StartHour && h < S.EndHour : h >= S.StartHour || h < S.EndHour;
    }

    public static void Apply(bool repin = false)
    {
        var active = S.Enabled && (!S.ScheduleOn || InNight());
        if (active) { GammaOK = Gamma.Apply(S.Kelvin, S.Brightness); gammaTouched = true; }
        else { if (gammaTouched) Gamma.Reset(); gammaTouched = false; GammaOK = true; }

        // Pin only if gamma verifiably applied, else pinning would make the screen brighter.
        if (S.FlickerFree && active && GammaOK)
        {
            if (S.Pinned is null || repin)
            {
                S.Pinned ??= new();
                S.Save(); // persist intent before touching hardware
                Backlight.Pin(S.Pinned);
            }
        }
        else if (S.Pinned is { } p) { Backlight.Restore(p); S.Pinned = null; }

        tray.Text = !S.Enabled ? "Dusk: off" : $"Dusk: {S.Kelvin:0}K, {S.Brightness * 100:0}%";
        popup.Sync();
        S.Save();
    }

    static void Restore()
    {
        if (S.Pinned is { } p) { Backlight.Restore(p); S.Pinned = null; S.Save(); }
        if (gammaTouched) Gamma.Reset();
        gammaTouched = false;
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
            m.Items.Add("Open Dusk", null, (_, _) => popup.Toggle());
            m.Items.Add(new ToolStripMenuItem("On", null, (_, _) => { S.Enabled = !S.Enabled; Apply(); }) { Checked = S.Enabled });
            m.Items.Add(new ToolStripSeparator());
            foreach (var (name, k) in Presets)
                m.Items.Add(new ToolStripMenuItem($"{name}  {k}K", null, (_, _) => { S.Kelvin = k; S.Enabled = true; Apply(); }) { Checked = S.Kelvin == k });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Quit Dusk", null, (_, _) => Application.Exit());
        };
        m.Items.Add("…"); // Opening only fires when the strip has items
        return m;
    }
}
