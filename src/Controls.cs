using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using static Dusk.Theme;

namespace Dusk;

/// Damped spring driven by the render loop, standing in for SwiftUI's .spring(response:dampingFraction:).
sealed class Spring
{
    static readonly List<Spring> live = new();
    static TimeSpan last;
    public double Value, Target;
    double vel;
    readonly double k, c;
    readonly Action changed;

    public Spring(double value, double response, double damping, Action changed)
    {
        Value = Target = value;
        var w = 2 * Math.PI / response;
        k = w * w; c = 2 * damping * w;
        this.changed = changed;
    }

    public void To(double target, bool instant = false)
    {
        Target = target;
        if (instant) { Value = target; vel = 0; changed(); return; }
        if (live.Contains(this)) return;
        if (live.Count == 0) { last = TimeSpan.Zero; CompositionTarget.Rendering += Step; }
        live.Add(this);
    }

    static void Step(object? s, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = last == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - last).TotalSeconds, 1 / 30.0);
        last = now;
        if (dt <= 0) return;
        foreach (var sp in live.ToArray())
        {
            sp.vel += (sp.k * (sp.Target - sp.Value) - sp.c * sp.vel) * dt;
            sp.Value += sp.vel * dt;
            if (Math.Abs(sp.Target - sp.Value) < 0.0005 && Math.Abs(sp.vel) < 0.0005) { sp.Value = sp.Target; sp.vel = 0; live.Remove(sp); }
            sp.changed();
        }
        if (live.Count == 0) CompositionTarget.Rendering -= Step;
    }
}

/// An element that draws itself with a callback. Used for layers that need their own Effect.
sealed class Layer : FrameworkElement
{
    public Action<DrawingContext, Size>? Paint;
    protected override void OnRender(DrawingContext dc) => Paint?.Invoke(dc, RenderSize);
}

static class Draw
{
    public static FormattedText Text(Visual v, string s, FontFamily f, double size, Brush b, int weight = 400) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(f, FontStyles.Normal, W(weight), FontStretches.Normal),
            size, b, VisualTreeHelper.GetDpi(v).PixelsPerDip);

    public static void Capsule(DrawingContext dc, Brush? fill, Pen? pen, Rect r) => dc.DrawRoundedRectangle(fill, pen, r, r.Height / 2, r.Height / 2);

    public static SolidColorBrush B(Color c) => new(c);

    /// Soft shadow without an Effect: a few stacked translucent shapes.
    public static void SoftShadow(DrawingContext dc, Rect r, double radius, double alpha, double dy)
    {
        for (var i = 3; i >= 1; i--)
        {
            var g = r; g.Inflate(i * 0.9, i * 0.9); g.Offset(0, dy);
            dc.DrawRoundedRectangle(B(A(Colors.Black, alpha / 3)), null, g, radius + i, radius + i);
        }
    }

    /// Brand mark: sun with horizontal slits in its lower half (Mac MenuIcon, flipped to top-down coordinates).
    public static Geometry SlitSun(double size)
    {
        var s = size / 18;
        var cuts = new GeometryGroup();
        foreach (var (y, h) in new[] { (9.5, 1.1), (11.6, 1.5), (13.7, 1.9) }) cuts.Children.Add(new RectangleGeometry(new Rect(0, y * s, size, h * s)));
        return new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Rect(1.5 * s, 1.5 * s, 15 * s, 15 * s)), cuts);
    }
}

/// Day/night switch: starry night track that floods with colour when on, a squishy knob that stretches on press,
/// follows a flick, and swaps its symbol.
sealed class GlowSwitch : FrameworkElement
{
    const double Wd = 46, Ht = 26, K = 22;
    bool on;
    Color tint;
    readonly string? onIcon, offIcon;
    readonly Spring pos, press;
    Point down;
    public event Action<bool>? Changed;

    public GlowSwitch(string name, Color tint, string? onIcon = null, string? offIcon = null)
    {
        Width = Wd; Height = Ht; Cursor = Cursors.Hand; Focusable = true;
        this.tint = tint; this.onIcon = onIcon; this.offIcon = offIcon;
        pos = new(0, 0.38, 0.66, InvalidateVisual);
        press = new(0, 0.22, 0.7, InvalidateVisual);
        AutomationProperties.SetName(this, name);
    }

    public bool IsOn { get => on; set { if (on == value) return; on = value; pos.To(on ? 1 : 0); } }
    public Color Tint { get => tint; set { tint = value; InvalidateVisual(); } }

    protected override void OnRender(DrawingContext dc)
    {
        var t = Math.Clamp(pos.Value, 0, 1);
        var track = new Rect(0, 0, Wd, Ht);
        Draw.Capsule(dc, new LinearGradientBrush(Color.FromRgb(26, 20, 61), Color.FromRgb(43, 31, 92), 0), null, track);
        Draw.Capsule(dc, new LinearGradientBrush(A(tint, 0.75), tint, 0) { Opacity = t }, null, track);

        // Three stars centred in the trailing 22px of the track; they shrink and fade as the switch turns on.
        var starA = (1 - t) * 0.75; var sc = 1 - 0.7 * t; var cx = Wd - 11; var cy = Ht / 2;
        foreach (var (dx, dy, d) in new[] { (-6.0, -5.0, 2.0), (-13.0, 4.0, 1.5), (-4.0, 5.0, 2.5) })
            dc.DrawEllipse(Draw.B(A(Colors.White, starA)), null, new Point(cx + dx * sc, cy + dy * sc), d / 2 * sc, d / 2 * sc);
        Draw.Capsule(dc, Draw.B(A(Colors.White, 0.35 * t)), null, new Rect(9, Ht / 2 - 1, 10, 2));
        dc.DrawRoundedRectangle(null, new Pen(Draw.B(A(Colors.Black, 0.35)), 1.5), new Rect(0.75, 1.5, Wd - 1.5, Ht - 2.25), Ht / 2, Ht / 2);
        dc.DrawRoundedRectangle(null, new Pen(Draw.B(A(Colors.White, 0.12 + 0.18 * t)), 0.5), new Rect(0.25, 0.25, Wd - 0.5, Ht - 0.5), Ht / 2, Ht / 2);

        var kw = K + 7 * press.Value;
        var knob = new Rect(2 + (Wd - 4 - kw) * pos.Value, 2, kw, K);
        Draw.SoftShadow(dc, knob, K / 2, 0.3, 1.5);
        Draw.Capsule(dc, Brushes.White, null, knob);
        var icon = on ? onIcon : offIcon;
        if (icon != null)
        {
            var ft = Draw.Text(this, icon, Icons, 10, Draw.B(on ? tint : Color.FromRgb(89, 77, 153)), 700);
            dc.DrawText(ft, new Point(knob.X + (knob.Width - ft.Width) / 2, knob.Y + (knob.Height - ft.Height) / 2));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { down = e.GetPosition(this); CaptureMouse(); press.To(1); Focus(); e.Handled = true; }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture(); press.To(0);
        var dx = e.GetPosition(this).X - down.X;
        Flip(Math.Abs(dx) > 8 ? dx > 0 : !on);
        e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.Key is Key.Space or Key.Enter) { Flip(!on); e.Handled = true; } }

    void Flip(bool next)
    {
        if (next == on) return;
        IsOn = next;
        Sound.Toggle(next);
        Changed?.Invoke(next);
    }
}

/// Rotary warmth dial: tick ring, glowing arc that grows as the screen warms, dark disc with the value.
/// Drag around the ring to turn; click the disc to toggle.
sealed class WarmthDial : Grid
{
    const double Size = 224, Start = 120, Sweep = 300, Lo = 500, Hi = 6500;
    const double R = Size / 2 - 2, Ra = R - 24, Rd = Ra - 17;
    static readonly Color[] ArcColors = { Sun, Ember, Rose, Crimson };
    static readonly double[] Presets = Program.Presets.Select(p => (double)p.Kelvin).ToArray();

    double kelvin = 3400;
    bool enabled = true, fromDisc, moved;
    double? lastSnap;
    int lastTick = -1;
    Point downAt;
    readonly Spring shown, drag, color;
    readonly Layer glow, face, disc;
    readonly TextBlock num, name;
    readonly ScaleTransform discScale = new(1, 1, Size / 2, Size / 2);
    public event Action<double>? KelvinChanged;
    public event Action? ToggleRequested;

    public WarmthDial()
    {
        Width = Size; Height = Size; Focusable = true; Background = Brushes.Transparent;
        AutomationProperties.SetName(this, "Warmth");
        ToolTip = "Drag the ring to set warmth. Click the center to turn Dusk on or off.";
        shown = new(P(kelvin), 0.38, 0.78, Repaint);
        drag = new(0, 0.3, 0.65, Repaint);
        color = new(1, 0.4, 1, Repaint);

        glow = new Layer { Paint = (dc, _) => Arc(dc, 13, true), Effect = new BlurEffect { Radius = 18 }, IsHitTestVisible = false };
        glow.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.55, 0.95, TimeSpan.FromSeconds(2.8))
        { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, EasingFunction = new System.Windows.Media.Animation.SineEase() });
        face = new Layer { Paint = (dc, _) => { Ticks(dc); Arc(dc, 13 + 2 * drag.Value, false); Knob(dc); }, IsHitTestVisible = false };
        disc = new Layer
        {
            Paint = (dc, _) => DiscPaint(dc), IsHitTestVisible = false, RenderTransform = discScale,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.5, BlurRadius = 20, ShadowDepth = 4, Direction = 270 },
        };
        num = new TextBlock { FontFamily = Serif, FontSize = 52, Foreground = Brushes.White };
        var k = new TextBlock { Text = "K", FontFamily = Serif, FontSize = 22, Foreground = Secondary, Margin = new(1, 0, 0, 0) };
        name = new TextBlock { FontFamily = SansW(600), FontWeight = W(600), FontSize = 11, Foreground = Draw.B(A(Colors.White, 0.55)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, -6, 0, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(num); row.Children.Add(k);
        k.VerticalAlignment = VerticalAlignment.Bottom; k.Margin = new(1, 0, 0, 9);
        var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Margin = new(0, 6, 0, 0) };
        label.Children.Add(row); label.Children.Add(name);
        foreach (var c in new UIElement[] { glow, face, disc, label }) Children.Add(c);
    }

    static double P(double k) => (Hi - k) / (Hi - Lo);
    void Repaint() { glow.InvalidateVisual(); face.InvalidateVisual(); disc.InvalidateVisual(); }
    Color Tone(Color c) => Gray(c, 1 - color.Value);

    public void Set(double k, bool on, string preset)
    {
        if (Math.Abs(k - kelvin) > 0.5 && !IsMouseCaptured) shown.To(P(k)); // animate preset jumps, follow drags 1:1
        kelvin = k; enabled = on;
        color.To(on ? 1 : 0);
        Opacity = on ? 1 : 0.55;
        num.Text = ((int)k).ToString(CultureInfo.InvariantCulture);
        name.Text = on ? preset : "Paused";
        AutomationProperties.SetHelpText(this, $"{(int)k} Kelvin, {preset}");
        Repaint();
    }

    static Point At(double deg, double r) { var a = deg * Math.PI / 180; return new(Size / 2 + Math.Cos(a) * r, Size / 2 + Math.Sin(a) * r); }

    static Color ArcColor(double f)
    {
        var x = Math.Clamp(f, 0, 1) * (ArcColors.Length - 1);
        var i = Math.Min((int)x, ArcColors.Length - 2);
        return Mix(ArcColors[i], ArcColors[i + 1], x - i);
    }

    void Ticks(DrawingContext dc)
    {
        var p = shown.Value; var cur = (int)Math.Round(p * 60);
        for (var i = 0; i <= 60; i++)
        {
            var f = i / 60.0; var a = Start + Sweep * f;
            bool major = i % 10 == 0, here = i == cur, lit = f <= p + 0.0001;
            var r1 = R - (here ? 15 : major ? 12 : 7);
            var c = here ? Colors.White : lit ? (major ? Sun : A(Ember, 0.9)) : A(Colors.White, major ? 0.28 : 0.14);
            dc.DrawLine(new Pen(Draw.B(Tone(c)), here ? 2.6 : major ? 2 : 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, At(a, r1), At(a, R));
        }
    }

    void Arc(DrawingContext dc, double width, bool glowOnly)
    {
        if (!glowOnly)
        {
            var track = new StreamGeometry();
            using (var g = track.Open()) { g.BeginFigure(At(Start, Ra), false, false); g.ArcTo(At(Start + Sweep, Ra), new Size(Ra, Ra), 0, true, SweepDirection.Clockwise, true, false); }
            dc.DrawGeometry(null, new Pen(Draw.B(A(Colors.White, 0.07)), 13) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, track);
        }
        var to = Math.Max(shown.Value, 0.004);
        var n = Math.Max(2, (int)(to * 150));
        for (var i = 0; i < n; i++)
        {
            double f0 = to * i / n, f1 = to * (i + 1) / n;
            var pen = new Pen(Draw.B(Tone(ArcColor((f0 + f1) / 2))), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, At(Start + Sweep * f0, Ra), At(Start + Sweep * f1, Ra));
        }
    }

    /// Glass bead riding the end of the arc.
    void Knob(DrawingContext dc)
    {
        var c = At(Start + Sweep * shown.Value, Ra);
        var d = 24 * (drag.Value > 0 && !fromDisc ? 1 + 0.2 * drag.Value : 1); var r = d / 2;
        var warm = Tone(Warm(kelvin));
        for (var i = 3; i >= 1; i--) dc.DrawEllipse(Draw.B(A(Colors.Black, 0.15)), null, new Point(c.X, c.Y + 2), r + i * 1.2, r + i * 1.2);
        dc.DrawEllipse(Draw.B(Color.FromArgb(235, 58, 54, 66)), null, c, r, r);
        dc.DrawEllipse(Draw.B(A(Colors.White, 0.16)), null, c, r, r);
        dc.DrawEllipse(new RadialGradientBrush(A(warm, 0.55), A(warm, 0.1)) { GradientOrigin = new(0.5, 0.75), Center = new(0.5, 0.75) }, null, c, r, r);
        dc.DrawEllipse(new LinearGradientBrush(new GradientStopCollection { new(A(Colors.White, 0.65), 0), new(A(Colors.White, 0.05), 0.5), new(Colors.Transparent, 1) }, 90), null, c, r - 1.5, r - 1.5);
        dc.DrawEllipse(Draw.B(A(Colors.White, 0.85)), null, new Point(c.X, c.Y - d * 0.27), d * 0.21, d * 0.08);
        dc.DrawEllipse(null, new Pen(new LinearGradientBrush(A(Colors.White, 0.9), A(Colors.White, 0.15), 90), 1), c, r - 0.5, r - 0.5);
    }

    void DiscPaint(DrawingContext dc)
    {
        var c = new Point(Size / 2, Size / 2);
        dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(43, 43, 43), Color.FromRgb(15, 15, 15)) { GradientOrigin = new(0.42, 0.35), Center = new(0.42, 0.35), RadiusX = 0.55, RadiusY = 0.55 }, null, c, Rd, Rd);
        dc.DrawEllipse(new RadialGradientBrush(A(Tone(Warm(kelvin)), 0.16), Colors.Transparent), null, c, Rd, Rd);
        dc.DrawEllipse(null, new Pen(new LinearGradientBrush(A(Colors.Black, 0.45), Colors.Transparent, 90), 3), c, Rd - 1.5, Rd - 1.5);
        dc.DrawEllipse(null, new Pen(Draw.B(A(Colors.White, 0.08)), 0.5), c, Rd - 0.25, Rd - 0.25);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        if (Math.Abs((p - new Point(Size / 2, Size / 2)).Length) > R + 4) return;
        CaptureMouse(); Focus();
        downAt = p; moved = false;
        fromDisc = (p - new Point(Size / 2, Size / 2)).Length < Rd - 4;
        drag.To(1);
        if (fromDisc) discScale.ScaleX = discScale.ScaleY = 0.97; else Sound.Press();
        Turn(p);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        var p = e.GetPosition(this);
        if ((p - downAt).Length >= 4) moved = true;
        Turn(p);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        discScale.ScaleX = discScale.ScaleY = 1;
        drag.To(0);
        if (fromDisc && !moved) ToggleRequested?.Invoke();
        e.Handled = true;
    }

    void Turn(Point v)
    {
        if (fromDisc || !enabled) return;
        var rel = (Math.Atan2(v.Y - Size / 2, v.X - Size / 2) * 180 / Math.PI - Start) % 360;
        if (rel < 0) rel += 360;
        if (rel > Sweep) rel = rel > Sweep + (360 - Sweep) / 2 ? 0 : Sweep;
        var k = Hi - rel / Sweep * (Hi - Lo);
        var s = Presets.MinBy(x => Math.Abs(x - k));
        if (Math.Abs(s - k) < 120)
        {
            k = s;
            if (lastSnap != s) { lastSnap = s; Sound.Snap(); }
        }
        else lastSnap = null;
        k = Math.Round(k / 10) * 10;
        var tick = (int)Math.Round(P(k) * 60);
        if (tick != lastTick && (lastSnap == null || lastSnap != k)) { lastTick = tick; Sound.Tick(tick / 60.0); }
        shown.To(P(k), instant: true);
        if (k != kelvin) KelvinChanged?.Invoke(k);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var k = e.Key switch { Key.Up or Key.Right => Math.Max(kelvin - 300, Lo), Key.Down or Key.Left => Math.Min(kelvin + 300, Hi), _ => kelvin };
        if (e.Key is Key.Space or Key.Enter) { ToggleRequested?.Invoke(); e.Handled = true; }
        else if (k != kelvin) { KelvinChanged?.Invoke(k); e.Handled = true; }
    }
}

/// Brightness as a rising staircase of light bars. Click or drag anywhere; each step ticks with a rising pitch,
/// every 25% gives a firmer click. Hover previews where you'll land.
sealed class BrightnessBars : Grid
{
    const int N = 20; const double Gap = 3, Ht = 36;
    double value = 1;
    Color tint = Colors.White;
    int? hover;
    int last = -1;
    readonly Spring[] lit = new Spring[N];
    readonly Spring lift, icon;
    readonly Layer bars;
    readonly TextBlock sun, pct;
    public event Action<double>? Changed;

    public BrightnessBars()
    {
        Focusable = true;
        AutomationProperties.SetName(this, "Brightness");
        ColumnDefinitions.Add(new() { Width = new(22) });
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new() { Width = new(48) });
        sun = new TextBlock { Text = GSun, FontFamily = Icons, FontWeight = W(600), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        bars = new Layer { Height = Ht, Margin = new(10, 0, 10, 0), Paint = (dc, sz) => Paint(dc, sz), Cursor = Cursors.Hand };
        pct = new TextBlock { FontFamily = MonoW(500), FontWeight = W(500), FontSize = 12, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        SetColumn(bars, 1); SetColumn(pct, 2);
        Children.Add(sun); Children.Add(bars); Children.Add(pct);
        for (var i = 0; i < N; i++) lit[i] = new(1, 0.32, 0.62, bars.InvalidateVisual);
        lift = new(0, 0.3, 0.65, bars.InvalidateVisual);
        icon = new(1, 0.3, 0.6, () => sun.FontSize = 11 + 7 * icon!.Value);

        bars.MouseLeftButtonDown += (_, e) => { bars.CaptureMouse(); Focus(); lift.To(1); Pick(e.GetPosition(bars).X); e.Handled = true; };
        bars.MouseMove += (_, e) =>
        {
            var x = e.GetPosition(bars).X;
            hover = Index(x); bars.InvalidateVisual();
            if (bars.IsMouseCaptured) Pick(x);
        };
        bars.MouseLeftButtonUp += (_, _) => { bars.ReleaseMouseCapture(); lift.To(0); };
        bars.MouseLeave += (_, _) => { hover = null; bars.InvalidateVisual(); };
    }

    int Lit => (int)Math.Round(value * N);
    double Bw => (bars.ActualWidth - Gap * (N - 1)) / N;
    int Index(double x) => Math.Clamp((int)(x / (Bw + Gap)), 0, N - 1);

    public void Set(double v, Color t, bool animate = true)
    {
        value = v; tint = t;
        for (var i = 0; i < N; i++) lit[i].To(i < Lit ? 1 : 0, !animate);
        icon.To(v, !animate);
        sun.Foreground = Draw.B(t);
        pct.Text = $"{Math.Round(v * 100)}%";
        AutomationProperties.SetHelpText(this, $"{Math.Round(v * 100)} percent");
        bars.InvalidateVisual();
    }

    void Paint(DrawingContext dc, Size sz)
    {
        var bw = Bw;
        if (bw <= 0) return;
        var rad = Math.Min(bw / 2, 3);
        var litBrush = new LinearGradientBrush(tint, A(tint, 0.55), 90);
        for (var i = 0; i < N; i++)
        {
            var h = 8 + (sz.Height - 8) * (i + 1) / N;
            if (i == Lit - 1) h *= 1 + 0.15 * lift.Value;
            var r = new Rect(i * (bw + Gap), sz.Height - h, bw, h);
            var hv = hover == i;
            dc.DrawRoundedRectangle(Draw.B(A(Colors.White, hv ? 0.22 : 0.09)), hv ? new Pen(Draw.B(A(Colors.White, 0.6)), 1) : null, r, rad, rad);
            if (lit[i].Value > 0.01)
            {
                litBrush = litBrush.Clone(); litBrush.Opacity = Math.Clamp(lit[i].Value, 0, 1);
                dc.DrawRoundedRectangle(litBrush, null, r, rad, rad);
            }
        }
    }

    void Pick(double x) => Step(Index(x));

    void Step(int i)
    {
        var k = Math.Clamp(i, 0, N - 1);
        if (k != last)
        {
            last = k;
            if ((k + 1) % 5 == 0) Sound.Snap(); else Sound.Tick(k / (double)(N - 1));
        }
        var v = (k + 1) / (double)N;
        if (Math.Abs(v - value) > 0.001) Changed?.Invoke(v);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Right or Key.Up) { Step(Lit); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Down) { Step(Lit - 2); e.Handled = true; }
    }
}

/// Preset chip: warm dot plus name; selected chips glow in their own color.
sealed class Chip : FrameworkElement
{
    readonly string name;
    readonly Color warm;
    readonly Spring sel, press;
    readonly ScaleTransform scale = new(1, 1);
    public event Action? Click;

    public Chip(string name, int kelvin)
    {
        this.name = name; warm = Warm(kelvin);
        sel = new(0, 0.38, 0.78, InvalidateVisual);
        press = new(1, 0.25, 0.6, () => { scale.ScaleX = scale.ScaleY = press!.Value; });
        Cursor = Cursors.Hand; Focusable = true;
        RenderTransform = scale; RenderTransformOrigin = new(0.5, 0.5);
        AutomationProperties.SetName(this, $"{name} {kelvin}K");
    }

    public bool Selected { set => sel.To(value ? 1 : 0); }

    FormattedText Label => Draw.Text(this, name, SansW(500), 12, Draw.B(Mix(A(Colors.White, 0.55), Colors.White, Math.Clamp(sel.Value, 0, 1))), 500);
    protected override Size MeasureOverride(Size _) { var t = Label; return new(10 + 9 + 6 + t.Width + 10, 12 + Math.Max(9, t.Height)); }

    protected override void OnRender(DrawingContext dc)
    {
        var r = new Rect(RenderSize); var s = Math.Clamp(sel.Value, 0, 1);
        Draw.Capsule(dc, Draw.B(A(Colors.White, 0.06)), null, r);
        if (s > 0.01) Draw.Capsule(dc, Draw.B(A(warm, 0.28 * s)), new Pen(Draw.B(A(warm, 0.6 * s)), 1), new Rect(0.5, 0.5, r.Width - 1, r.Height - 1));
        var dot = new Point(10 + 4.5, r.Height / 2);
        if (s > 0.01) dc.DrawEllipse(new RadialGradientBrush(A(warm, 0.8 * s), Colors.Transparent), null, dot, 9, 9);
        dc.DrawEllipse(Draw.B(warm), null, dot, 4.5, 4.5);
        var t = Label;
        dc.DrawText(t, new Point(25, (r.Height - t.Height) / 2));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { CaptureMouse(); press.To(0.94); Sound.Press(); e.Handled = true; }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture(); press.To(1);
        if (new Rect(RenderSize).Contains(e.GetPosition(this))) Click?.Invoke();
    }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.Key is Key.Space or Key.Enter) { Click?.Invoke(); e.Handled = true; } }
}

/// Wraps children into centered rows (SwiftUI FlowLayout from the Mac app).
sealed class CenteredWrap : Panel
{
    public double Spacing { get; init; } = 6;

    List<(List<UIElement> items, double w, double h)> Rows(double width)
    {
        var rows = new List<(List<UIElement>, double, double)>();
        var cur = new List<UIElement>(); double w = 0, h = 0;
        foreach (UIElement c in InternalChildren)
        {
            var s = c.DesiredSize;
            if (cur.Count > 0 && w + Spacing + s.Width > width) { rows.Add((cur, w, h)); cur = new(); w = 0; h = 0; }
            w += (cur.Count == 0 ? 0 : Spacing) + s.Width; h = Math.Max(h, s.Height); cur.Add(c);
        }
        if (cur.Count > 0) rows.Add((cur, w, h));
        return rows;
    }

    protected override Size MeasureOverride(Size avail)
    {
        foreach (UIElement c in InternalChildren) c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var rows = Rows(avail.Width);
        return new(double.IsInfinity(avail.Width) ? rows.Max(r => r.w) : avail.Width, rows.Sum(r => r.h) + Spacing * Math.Max(rows.Count - 1, 0));
    }

    protected override Size ArrangeOverride(Size size)
    {
        double y = 0;
        foreach (var (items, w, h) in Rows(size.Width))
        {
            var x = (size.Width - w) / 2;
            foreach (var c in items) { c.Arrange(new Rect(new Point(x, y), c.DesiredSize)); x += c.DesiredSize.Width + Spacing; }
            y += h + Spacing;
        }
        return size;
    }
}

/// Press-to-shrink wrapper for plain buttons (Mac PressStyle).
static class Press
{
    public static T Squish<T>(T el, Action click) where T : FrameworkElement
    {
        var sc = new ScaleTransform(1, 1);
        el.RenderTransform = sc; el.RenderTransformOrigin = new(0.5, 0.5); el.Cursor = Cursors.Hand;
        el.MouseLeftButtonDown += (_, e) => { el.CaptureMouse(); Animate(sc, ScaleTransform.ScaleXProperty, 0.94, 120); Animate(sc, ScaleTransform.ScaleYProperty, 0.94, 120); Sound.Press(); e.Handled = true; };
        el.MouseLeftButtonUp += (_, e) =>
        {
            if (!el.IsMouseCaptured) return;
            el.ReleaseMouseCapture();
            Animate(sc, ScaleTransform.ScaleXProperty, 1, 260, Bounce(0.6)); Animate(sc, ScaleTransform.ScaleYProperty, 1, 260, Bounce(0.6));
            if (new Rect(el.RenderSize).Contains(e.GetPosition(el))) click();
        };
        return el;
    }
}

static class Ui
{
    public static TextBlock T(string s, double size, int weight = 400, Brush? fg = null, FontFamily? font = null) => new()
    {
        Text = s, FontSize = size, FontWeight = W(weight), FontFamily = font ?? SansW(weight), Foreground = fg ?? Brushes.White,
        TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock Glyph(string g, double size, Brush fg, int weight = 400) => new()
    {
        Text = g, FontFamily = Icons, FontSize = size, FontWeight = W(weight), Foreground = fg,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// Frosted card with a soft top highlight. The shadow sits on its own layer so text stays crisp.
    public static FrameworkElement Card(params UIElement[] children)
    {
        var stack = new StackPanel();
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i] is FrameworkElement fe && i > 0) fe.Margin = new(fe.Margin.Left, 12, fe.Margin.Right, fe.Margin.Bottom);
            stack.Children.Add(children[i]);
        }
        var g = new Grid();
        g.Children.Add(new Border
        {
            CornerRadius = new(16), Background = Draw.B(A(Colors.Black, 0.01)),
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.18, BlurRadius = 16, ShadowDepth = 4, Direction = 270 },
        });
        g.Children.Add(new Border
        {
            CornerRadius = new(16), Padding = new(12), Child = stack,
            Background = new LinearGradientBrush(A(Colors.White, 0.075), A(Colors.White, 0.02), 90),
            BorderThickness = new(0.8), BorderBrush = new LinearGradientBrush(A(Colors.White, 0.22), A(Colors.White, 0.04), 90),
        });
        return g;
    }

    public static StackPanel Stack(double spacing, params UIElement[] children)
    {
        var s = new StackPanel();
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i] is FrameworkElement fe && i > 0) fe.Margin = new(fe.Margin.Left, spacing, fe.Margin.Right, fe.Margin.Bottom);
            s.Children.Add(children[i]);
        }
        return s;
    }

    /// Icon tile + title/subtitle + switch, like the Mac ToggleRow.
    public static (FrameworkElement row, GlowSwitch sw, Action<bool> sync) ToggleRow(string glyph, string title, string sub, Color tint)
    {
        var icon = Glyph(glyph, 13, Secondary, 600);
        var tile = new Border { Width = 30, Height = 30, CornerRadius = new(9), Child = icon, RenderTransform = new ScaleTransform(1, 1, 15, 15) };
        var text = Stack(1, T(title, 13, 500), T(sub, 11, 400, Secondary));
        text.VerticalAlignment = VerticalAlignment.Center; text.Margin = new(10, 0, 4, 0);
        var sw = new GlowSwitch(title, tint) { VerticalAlignment = VerticalAlignment.Center };
        var g = new Grid();
        g.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(text, 1); Grid.SetColumn(sw, 2);
        g.Children.Add(tile); g.Children.Add(text); g.Children.Add(sw);
        void Sync(bool on)
        {
            sw.IsOn = on;
            icon.Foreground = on ? Draw.B(tint) : Secondary;
            tile.Background = Draw.B(A(tint, on ? 0.2 : 0.07));
        }
        sw.Changed += on =>
        {
            Sync(on);
            var st = (ScaleTransform)tile.RenderTransform;
            var bounce = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
            bounce.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.18, System.Windows.Media.Animation.KeyTime.FromPercent(0.35)));
            bounce.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1, System.Windows.Media.Animation.KeyTime.FromPercent(1), Bounce(0.5)));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, bounce); st.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
        };
        return (g, sw, Sync);
    }

    /// Fine film grain, generated once and tiled.
    public static Brush Grain()
    {
        const int n = 96;
        var px = new byte[n * n];
        new Random(3).NextBytes(px);
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(n, n, 96, 96, PixelFormats.Gray8, null, px, n);
        return new ImageBrush(bmp) { TileMode = TileMode.Tile, Viewport = new(0, 0, n, n), ViewportUnits = BrushMappingMode.Absolute, Opacity = 0.03 };
    }
}
