using Drawing = System.Drawing;
using static Dusk.Program;

namespace Dusk;

/// Small panel that opens from the tray icon, like the Mac menu-bar popover.
sealed class Popup : Form
{
    static readonly Drawing.Color Night = Hex(0x0E0A2A), Indigo = Hex(0x2A1D66), Rose = Hex(0xF2507B),
        Ember = Hex(0xFF9A4D), Sun = Hex(0xFFE08A), Soft = Hex(0xC9C3E6);
    static Drawing.Color Hex(int v) => Drawing.Color.FromArgb(v >> 16, (v >> 8) & 255, v & 255);

    readonly FlowLayoutPanel root;
    readonly CheckBox on, flicker, schedule, startup;
    readonly TrackBar warmth, bright;
    readonly Label warmthLabel, brightLabel, warning;
    readonly Button unlock;
    readonly NumericUpDown from, to;
    readonly FlowLayoutPanel presets;
    readonly int inner;
    bool syncing;
    DateTime lastToggle;

    public Popup()
    {
        _ = Handle; // real DeviceDpi for sizing, and tray events marshal through this window
        inner = LogicalToDeviceUnits(288);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Night;
        ForeColor = Sun;
        Font = new Drawing.Font("Segoe UI", 9.5f);
        // Flow layout sizes rows from the controls themselves, so any DPI or font size stacks cleanly.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new(LogicalToDeviceUnits(12));
        root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        Controls.Add(root);
        Deactivate += (_, _) => { Hide(); lastToggle = DateTime.Now; };

        Add(new Label { Text = "Dusk", Font = new Drawing.Font("Georgia", 16f), AutoSize = true });
        on = Add(Check("On", v => S.Enabled = v));

        warmthLabel = Add(Lbl(""));
        warmth = Add(Slider(5, 65, v => S.Kelvin = v * 100));
        presets = Add(new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MaximumSize = new(inner, 0) });
        foreach (var (name, k) in Presets)
        {
            var b = new Button { Text = name, Tag = k, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Indigo, Margin = new(0, 0, 4, 4) };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => { S.Kelvin = k; S.Enabled = true; Apply(); };
            presets.Controls.Add(b);
        }

        brightLabel = Add(Lbl(""));
        bright = Add(Slider(5, 100, v => S.Brightness = v / 100.0));

        flicker = Add(Check("Flicker-free", v => S.FlickerFree = v));
        Add(Hint("Backlight at 100%, dim in software"));
        schedule = Add(Check("Schedule", v => S.ScheduleOn = v));
        Add(Hint("Warm up automatically at night"));
        var hours = Add(new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink });
        from = Hour(h => S.StartHour = h);
        to = Hour(h => S.EndHour = h);
        hours.Controls.AddRange(new Control[] { Lbl("From"), from, Lbl("to"), to });
        // Not via Check(): Apply() re-syncs from the registry, which must be written first.
        startup = Add(new CheckBox { Text = "Start Dusk when you sign in", AutoSize = true });
        startup.CheckedChanged += (_, _) => { if (!syncing) Startup.On = startup.Checked; };

        warning = Add(new Label { ForeColor = Rose, AutoSize = true, MaximumSize = new(inner, 0) });
        unlock = Add(new Button { Text = "Unlock full range (admin, once)", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Indigo });
        unlock.Click += (_, _) =>
        {
            if (!Gamma.Unlock()) return;
            Apply();
            if (!GammaOK) MessageBox.Show("Range unlocked. Restart Windows if warm settings still don't apply.", "Dusk");
        };

        var quit = Add(new LinkLabel { Text = "Quit Dusk", AutoSize = true, LinkColor = Soft });
        quit.Click += (_, _) => Application.Exit();
    }

    public void Toggle()
    {
        // One tray click can both deactivate (mouse down) and toggle (mouse up); a double click toggles twice.
        if ((DateTime.Now - lastToggle).TotalMilliseconds < SystemInformation.DoubleClickTime) return;
        lastToggle = DateTime.Now;
        if (Visible) { Hide(); return; }
        Sync();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new(area.Right - Width - 12, area.Bottom - Height - 12);
        Show();
        Activate();
    }

    public void Sync()
    {
        syncing = true;
        on.Checked = S.Enabled;
        warmth.Value = Math.Clamp((int)Math.Round(S.Kelvin / 100), 5, 65);
        warmthLabel.Text = $"Warmth  {S.Kelvin:0}K";
        bright.Value = Math.Clamp((int)Math.Round(S.Brightness * 100), 5, 100);
        brightLabel.Text = $"Brightness  {S.Brightness * 100:0}%";
        foreach (Button b in presets.Controls) b.ForeColor = (int)b.Tag! == (int)S.Kelvin ? Ember : Sun;
        flicker.Checked = S.FlickerFree;
        schedule.Checked = S.ScheduleOn;
        from.Value = S.StartHour;
        to.Value = S.EndHour;
        startup.Checked = Startup.On;
        warning.Visible = !GammaOK;
        unlock.Visible = !GammaOK && !Gamma.Unlocked;
        warning.Text = Gamma.Unlocked
            ? "Windows or your GPU driver didn't take this setting. HDR blocks it."
            : "Windows blocked this setting. Unlock the full range to go warmer and dimmer.";
        syncing = false;
    }

    T Add<T>(T c) where T : Control
    {
        root.Controls.Add(c);
        return c;
    }

    static Label Lbl(string s) => new() { Text = s, AutoSize = true, Margin = new(0, 6, 4, 0) };

    Label Hint(string s) => new() { Text = s, AutoSize = true, ForeColor = Soft, MaximumSize = new(inner, 0), Margin = new(LogicalToDeviceUnits(18), 0, 0, 6) };

    CheckBox Check(string text, Action<bool> set)
    {
        var c = new CheckBox { Text = text, AutoSize = true };
        c.CheckedChanged += (_, _) => { if (syncing) return; set(c.Checked); Apply(); };
        return c;
    }

    TrackBar Slider(int min, int max, Action<int> set)
    {
        var t = new TrackBar { Minimum = min, Maximum = max, Width = inner, TickStyle = TickStyle.None, BackColor = Night };
        t.ValueChanged += (_, _) => { if (syncing) return; set(t.Value); Apply(); };
        return t;
    }

    NumericUpDown Hour(Action<int> set)
    {
        var n = new NumericUpDown { Minimum = 0, Maximum = 23, Width = LogicalToDeviceUnits(52), BackColor = Indigo, ForeColor = Sun };
        n.ValueChanged += (_, _) => { if (syncing) return; set((int)n.Value); Apply(); };
        return n;
    }
}
