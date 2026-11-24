using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using static Dusk.Program;
using Path = System.Windows.Shapes.Path;
using static Dusk.Theme;

namespace Dusk;

/// The panel that opens from the tray icon. Same layout, controls and motion as the Mac menu-bar window.
sealed class DuskWindow : Window
{
    const double PanelW = 320, Pad = 16, ShadowPad = 24;

    readonly GlowSwitch power;
    readonly WarmthDial dial;
    readonly BrightnessBars bars;
    readonly List<(Chip chip, int kelvin)> chips = new();
    readonly FrameworkElement mainCard, mainPage, settingsPage;
    readonly Grid pages;
    readonly TextBlock title, settingsGlyph, mainWarn, settingsWarn;
    readonly FrameworkElement unlockMain, unlockSettings, hours;
    readonly Action<bool> syncFlicker, syncSchedule, syncLogin, syncSounds;
    readonly TextBlock fromText, toText;
    readonly GradientStop tintStop = new(Colors.Transparent, 0);
    readonly Layer dust;
    readonly Border root;
    bool settings;
    DateTime hiddenAt;

    public DuskWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight; ShowActivated = true; Title = "Dusk";
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        // Header: slit-sun mark, title, settings button, power switch.
        var mark = new Path
        {
            Data = Draw.SlitSun(20), Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center,
            Fill = new LinearGradientBrush(new GradientStopCollection { new(Color.FromRgb(255, 224, 138), 0), new(Color.FromRgb(255, 153, 77), 0.5), new(Color.FromRgb(242, 79, 122), 1) }, 90),
        };
        title = Ui.T("Dusk", 22, font: Serif); title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new(8, 0, 0, 0);
        settingsGlyph = Ui.Glyph(GSliders, 12, Secondary, 600);
        var gear = Press.Squish(new Border
        {
            Width = 28, Height = 28, CornerRadius = new(14), Child = settingsGlyph, Background = Draw.B(A(Colors.White, 0.08)),
            BorderBrush = Draw.B(A(Colors.White, 0.12)), BorderThickness = new(0.6), ToolTip = "Settings", Margin = new(0, 0, 8, 0),
        }, () => ShowSettings(!settings));
        power = new GlowSwitch("Dusk", Ember, GSun, GMoon) { VerticalAlignment = VerticalAlignment.Center };
        power.Changed += on => { S.Enabled = on; Apply(); };
        var header = new Grid();
        foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            header.ColumnDefinitions.Add(new() { Width = w });
        Grid.SetColumn(title, 1); Grid.SetColumn(gear, 3); Grid.SetColumn(power, 4);
        header.Children.Add(mark); header.Children.Add(title); header.Children.Add(gear); header.Children.Add(power);

        // Main page: dial, then a card with brightness and presets.
        dial = new WarmthDial { HorizontalAlignment = HorizontalAlignment.Center };
        dial.KelvinChanged += k => { S.Kelvin = k; Apply(); };
        dial.ToggleRequested += () => { S.Enabled = !S.Enabled; Sound.Toggle(S.Enabled); Apply(); };
        bars = new BrightnessBars();
        bars.Changed += v => { S.Brightness = Math.Max(v, 0.05); Apply(); };
        var wrap = new CenteredWrap { Spacing = 6 };
        foreach (var (name, k) in Presets)
        {
            var c = new Chip(name, k);
            c.Click += () => { S.Kelvin = k; Apply(); };
            chips.Add((c, k)); wrap.Children.Add(c);
        }
        var brightLabel = new StackPanel { Orientation = Orientation.Horizontal };
        brightLabel.Children.Add(Ui.Glyph(GSunLow, 12, Draw.B(A(Colors.White, 0.85)), 600));
        var bl = Ui.T("Brightness", 12, 600, Draw.B(A(Colors.White, 0.85))); bl.Margin = new(6, 0, 0, 0); brightLabel.Children.Add(bl);
        // Windows-only: the default gamma range blocks warm/dim settings, so say so where the change was made.
        mainWarn = Ui.T("", 11, 400, Draw.B(Orange));
        unlockMain = Pill("Unlock warmer and dimmer", UnlockRange);
        mainCard = Ui.Card(brightLabel, bars, wrap, Ui.Stack(6, mainWarn, unlockMain));
        mainPage = Ui.Stack(14, dial, mainCard);

        // Settings page: three cards of toggle rows.
        var (flickRow, flickSw, fSync) = Ui.ToggleRow(GPulse, "Flicker-free", "Backlight at 100%, dim in software", Mint);
        flickSw.Changed += v => { S.FlickerFree = v; Apply(); };
        settingsWarn = Ui.T("Windows ignored the color change, so the backlight was left alone.", 11, 400, Draw.B(Orange));
        unlockSettings = Pill("Unlock warmer and dimmer", UnlockRange);
        var (schedRow, schedSw, sSync) = Ui.ToggleRow(GClock, "Schedule", "Warm up automatically at night", Orange);
        schedSw.Changed += v => { S.ScheduleOn = v; Apply(); };
        fromText = Ui.T("", 12, 500); toText = Ui.T("", 12, 500);
        var hp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        hp.Children.Add(HourPicker(fromText, h => S.StartHour = h, () => S.StartHour));
        var arrow = Ui.Glyph(GArrow, 10, Secondary); arrow.Margin = new(8, 0, 8, 0); hp.Children.Add(arrow);
        hp.Children.Add(HourPicker(toText, h => S.EndHour = h, () => S.EndHour));
        hours = hp;
        var (loginRow, loginSw, lSync) = Ui.ToggleRow(GPower, "Launch at login", "Start Dusk when you sign in", Ember);
        loginSw.Changed += v => { Startup.On = v; lSync(Startup.On); };
        var (soundRow, soundSw, soSync) = Ui.ToggleRow(GSpeaker, "Sounds", "Soft clicks on every control", Pink);
        soundSw.Changed += v => { S.Sounds = v; Apply(); };
        syncFlicker = fSync; syncSchedule = sSync; syncLogin = lSync; syncSounds = soSync;
        settingsPage = Ui.Stack(12,
            Ui.Card(flickRow, Warn(settingsWarn), unlockSettings),
            Ui.Card(schedRow, hours),
            Ui.Card(loginRow, soundRow));

        // Pages share one slot sized to the main page, and slide sideways like the Mac app.
        pages = new Grid();
        settingsPage.VerticalAlignment = VerticalAlignment.Top;
        settingsPage.Opacity = 0; settingsPage.IsHitTestVisible = false; settingsPage.RenderTransform = new TranslateTransform(40, 0);
        mainPage.RenderTransform = new TranslateTransform(0, 0);
        pages.Children.Add(mainPage); pages.Children.Add(settingsPage);
        mainPage.SizeChanged += (_, e) => pages.Height = e.NewSize.Height;

        // Footer: credit and Quit.
        var credit = new TextBlock { FontFamily = Sans, FontSize = 10.5, Foreground = Tertiary, Cursor = Cursors.Hand, ToolTip = "justaziz.com", VerticalAlignment = VerticalAlignment.Center };
        credit.Inlines.Add("Built with ");
        credit.Inlines.Add(new System.Windows.Documents.Run(GHeart) { FontFamily = Icons, Foreground = Draw.B(Ember), FontSize = 9.5 });
        credit.Inlines.Add(" ");
        credit.Inlines.Add(new System.Windows.Documents.Underline(new System.Windows.Documents.Run("by Aziz")));
        credit.MouseLeftButtonUp += (_, _) => Open("https://justaziz.com?utm_source=dusk-windows");
        var quit = Press.Squish(Ui.T("Quit", 11, 500, Secondary), Quit);
        quit.HorizontalAlignment = HorizontalAlignment.Right;
        var footer = new Grid();
        footer.Children.Add(credit); footer.Children.Add(quit);

        var content = Ui.Stack(14, header, pages, footer);
        content.Margin = new(Pad); content.Width = PanelW - 2 * Pad;

        // Background: warm wash from the top, drifting dust, film grain.
        var wash = new Rectangle { Fill = new LinearGradientBrush(new GradientStopCollection { tintStop, new(Colors.Transparent, 0.5) }, 90), IsHitTestVisible = false };
        dust = new Layer { IsHitTestVisible = false, Paint = PaintDust };
        var grain = new Rectangle { Fill = Ui.Grain(), IsHitTestVisible = false };
        var layers = new Grid();
        foreach (var l in new UIElement[] { wash, dust, grain, content }) layers.Children.Add(l);

        root = new Border
        {
            CornerRadius = new(20), Background = Draw.B(PanelBg), Child = layers, ClipToBounds = true,
            BorderBrush = Draw.B(A(Colors.White, 0.08)), BorderThickness = new(1),
        };
        root.SizeChanged += (_, e) => root.Clip = new RectangleGeometry(new Rect(e.NewSize), 20, 20);
        var shadow = new Border { CornerRadius = new(20), Background = Draw.B(PanelBg), Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.45, BlurRadius = 28, ShadowDepth = 6, Direction = 270 } };
        var outer = new Grid { Margin = new(ShadowPad) };
        outer.Children.Add(shadow); outer.Children.Add(root);
        Content = outer;

        Deactivated += (_, _) => { Hide(); hiddenAt = DateTime.Now; };
        // Anchored above the tray, so grow upward when rows appear (the Mac popover hangs from the menu bar instead).
        SizeChanged += (_, e) => { if (IsVisible && e.HeightChanged) Top -= e.NewSize.Height - e.PreviousSize.Height; };
        IsVisibleChanged += (_, _) => { if (IsVisible) CompositionTarget.Rendering += Tick; else CompositionTarget.Rendering -= Tick; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { if (settings) ShowSettings(false); else Hide(); e.Handled = true; }
            else if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control) Quit();
        };
    }

    static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { } }

    static void Quit() => System.Windows.Application.Current.Shutdown();

    void UnlockRange()
    {
        if (!Gamma.Unlock()) return;
        Apply();
        if (!GammaOK) MessageBox.Show(this, "Range unlocked. Restart Windows if warm settings still don't apply.", "Dusk");
    }

    static FrameworkElement Warn(TextBlock t)
    {
        var row = new DockPanel();
        var g = Ui.Glyph(GWarn, 11, Draw.B(Orange)); g.VerticalAlignment = VerticalAlignment.Top; g.Margin = new(0, 1, 6, 0);
        DockPanel.SetDock(g, Dock.Left);
        row.Children.Add(g); row.Children.Add(t);
        return row;
    }

    static FrameworkElement Pill(string text, Action click) => Press.Squish(new Border
    {
        CornerRadius = new(12), Padding = new(10, 5, 10, 5), HorizontalAlignment = HorizontalAlignment.Left,
        Background = Draw.B(A(Orange, 0.18)), BorderBrush = Draw.B(A(Orange, 0.5)), BorderThickness = new(1),
        Child = Ui.T(text, 11, 500, Draw.B(Orange)),
    }, click);

    /// "20:00" pill that opens a dark list of hours (Mac menu-style Picker).
    FrameworkElement HourPicker(TextBlock label, Action<int> set, Func<int> get)
    {
        var list = new StackPanel();
        var popup = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, PopupAnimation = PopupAnimation.Fade };
        var pill = new Border
        {
            CornerRadius = new(8), Padding = new(10, 4, 10, 4), Child = label,
            Background = Draw.B(A(Colors.White, 0.08)), BorderBrush = Draw.B(A(Colors.White, 0.12)), BorderThickness = new(0.6),
        };
        for (var h = 0; h < 24; h++)
        {
            var hour = h;
            var item = new Border { Padding = new(12, 4, 24, 4), CornerRadius = new(6), Child = Ui.T($"{h:00}:00", 12, 500), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            item.MouseEnter += (_, _) => item.Background = Draw.B(A(Ember, 0.25));
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            item.MouseLeftButtonUp += (_, _) => { popup.IsOpen = false; set(hour); Sound.Snap(); Apply(); };
            list.Children.Add(item);
        }
        popup.Child = new Border
        {
            Background = Draw.B(Color.FromRgb(36, 33, 46)), CornerRadius = new(10), Padding = new(4), BorderBrush = Draw.B(A(Colors.White, 0.1)), BorderThickness = new(1),
            Child = new ScrollViewer { Content = list, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden },
        };
        popup.PlacementTarget = pill;
        popup.Opened += (_, _) => ((ScrollViewer)((Border)popup.Child).Child).ScrollToVerticalOffset(Math.Max(0, get() - 3) * 26);
        return Press.Squish(pill, () => popup.IsOpen = true);
    }

    public void ShowSettingsPage() => ShowSettings(true);

    void ShowSettings(bool on)
    {
        if (on == settings) return;
        settings = on;
        title.Text = on ? "Settings" : "Dusk";
        settingsGlyph.Text = on ? GClose : GSliders;
        Slide(mainPage, on ? -40 : 0, on ? 0 : 1);
        Slide(settingsPage, on ? 0 : 40, on ? 1 : 0);
    }

    static void Slide(FrameworkElement page, double x, double opacity)
    {
        page.IsHitTestVisible = opacity > 0;
        Animate(page.RenderTransform, TranslateTransform.XProperty, x, 420, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 });
        Animate(page, OpacityProperty, opacity, 260);
    }

    public void Toggle()
    {
        if ((DateTime.Now - hiddenAt).TotalMilliseconds < 400) return; // the tray click that just closed it
        if (IsVisible) { Hide(); return; }
        Sync();
        Opacity = 0;
        Show();
        UpdateLayout();
        PlaceNearTray();
        Activate();
        Animate(this, OpacityProperty, 1, 160);
    }

    void PlaceNearTray()
    {
        var src = PresentationSource.FromVisual(this);
        var m = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var br = m.Transform(new Point(area.Right, area.Bottom));
        Left = br.X - ActualWidth + ShadowPad - 8;
        Top = br.Y - ActualHeight + ShadowPad - 8;
    }

    public void Sync()
    {
        var tint = Warm(S.Kelvin);
        var preset = Presets.FirstOrDefault(p => Math.Abs(p.Kelvin - S.Kelvin) < 1).Name ?? "Custom";
        power.IsOn = S.Enabled; power.Tint = tint;
        dial.Set(S.Kelvin, S.Enabled, preset);
        bars.Set(S.Brightness, tint, IsVisible);
        foreach (var (c, k) in chips) c.Selected = Math.Abs(k - S.Kelvin) < 1;
        mainCard.IsEnabled = S.Enabled;
        Animate(mainCard, OpacityProperty, S.Enabled ? 1 : 0.5, 300);
        tintStop.Color = A(tint, S.Enabled ? 0.26 : 0.04);
        syncFlicker(S.FlickerFree); syncSchedule(S.ScheduleOn); syncLogin(Startup.On); syncSounds(S.Sounds);
        hours.Visibility = S.ScheduleOn ? Visibility.Visible : Visibility.Collapsed;
        fromText.Text = $"{S.StartHour:00}:00"; toText.Text = $"{S.EndHour:00}:00";

        var blocked = !GammaOK;
        var canUnlock = blocked && !Gamma.Unlocked;
        mainWarn.Text = !Gamma.Unlocked ? "Windows blocked this setting."
            : Gamma.Reached > 0 ? $"Your display driver stops at about {Math.Round(Gamma.Reached * 100)}% of this setting, so Dusk set the warmest it allows. Restart Windows once if you just unlocked it."
            : "Windows or your display driver didn't take this setting. Restart Windows once after unlocking. HDR also blocks it.";
        ((FrameworkElement)mainWarn.Parent).Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        settingsWarn.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        ((FrameworkElement)settingsWarn.Parent).Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        unlockMain.Visibility = unlockSettings.Visibility = canUnlock ? Visibility.Visible : Visibility.Collapsed;
        dust.Opacity = S.Enabled ? 1 : 0.35;
    }

    // Slow warm dust motes drifting upward, like light at sunset. Only runs while visible, ~30 fps.
    TimeSpan lastDust;
    void Tick(object? s, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        if ((t - lastDust).TotalMilliseconds < 33) return;
        lastDust = t;
        dust.InvalidateVisual();
    }

    static double Rnd(int i, int k) { var x = Math.Sin(i * 127 + k * 311) * 43758.5453; return x - Math.Floor(x); }

    void PaintDust(DrawingContext dc, Size size)
    {
        var t = (DateTime.Now - DateTime.Today).TotalSeconds;
        var tint = Warm(S.Kelvin);
        for (var i = 0; i < 26; i++)
        {
            var speed = 4 + Rnd(i, 1) * 10;
            var y = size.Height - (Rnd(i, 2) * size.Height + t * speed) % (size.Height + 20) + 10;
            var x = Rnd(i, 3) * size.Width + Math.Sin(t * (0.2 + Rnd(i, 4) * 0.3) + i) * 10;
            var d = 1 + Rnd(i, 5) * 2.2;
            var a = 0.15 + 0.35 * (0.5 + 0.5 * Math.Sin(t * (0.6 + Rnd(i, 6)) + i));
            dc.DrawEllipse(Draw.B(A(tint, a * 0.25)), null, new Point(x, y), d * 2, d * 2);
            dc.DrawEllipse(Draw.B(A(Colors.White, a)), null, new Point(x, y), d * 0.5, d * 0.5);
        }
    }
}
