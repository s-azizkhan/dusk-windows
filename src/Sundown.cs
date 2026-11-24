using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static Dusk.Theme;

namespace Dusk;

/// "Sundown", same scene as the Mac app.
/// On: the slit sun sets behind a horizon, starry curtains sway shut, the seam flares and burns away into embers.
/// Off: the starry curtain fades in, parts from the middle, and the sun rises through the gap in a bloom.
sealed class SundownScene : FrameworkElement
{
    public const double Duration = 2.2;
    readonly DateTime start = DateTime.UtcNow;
    readonly bool on;

    public SundownScene(bool on)
    {
        this.on = on;
        IsHitTestVisible = false;
        CompositionTarget.Rendering += Tick;
    }

    public bool Done => (DateTime.UtcNow - start).TotalSeconds >= Duration;

    void Tick(object? s, EventArgs e)
    {
        InvalidateVisual();
        if (Done) CompositionTarget.Rendering -= Tick;
    }

    static double Ease(double x) => x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
    static double Seg(double x, double a, double b) => Math.Clamp((x - a) / (b - a), 0, 1);
    static double Rnd(int i, int k) { var v = Math.Sin(i * 12.9898 + k * 78.233) * 43758.5453; return v - Math.Floor(v); }
    static Brush Solid(Color c, double a) => new SolidColorBrush(A(c, a));
    static Brush Lin(Point p0, Point p1, params (Color c, double a)[] stops)
    {
        var g = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = p0, EndPoint = p1 };
        for (var i = 0; i < stops.Length; i++) g.GradientStops.Add(new(A(stops[i].c, stops[i].a), stops.Length == 1 ? 0 : i / (double)(stops.Length - 1)));
        return g;
    }
    static Brush Rad(Point c, double r, params (Color c, double a, double at)[] stops)
    {
        var g = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = c, GradientOrigin = c, RadiusX = r, RadiusY = r };
        foreach (var s in stops) g.GradientStops.Add(new(A(s.c, s.a), s.at));
        return g;
    }
    static Geometry Poly(IEnumerable<Point> pts, bool closed)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            var first = true;
            foreach (var p in pts) { if (first) { c.BeginFigure(p, closed, closed); first = false; } else c.LineTo(p, true, false); }
        }
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var t = (DateTime.UtcNow - start).TotalSeconds;
        var x = Math.Clamp(t / Duration, 0, 1);

        var close = on ? Ease(Seg(x, 0.08, 0.58)) : 1 - Ease(Seg(x, 0.18, 0.78));
        var veil = on ? 1 - Seg(x, 0.7, 0.95) : Seg(x, 0, 0.14) * (1 - Seg(x, 0.82, 1));
        var sunP = on ? Ease(Seg(x, 0, 0.62)) : 1 - Ease(Seg(x, 0.25, 0.85));
        var stars = on ? Seg(x, 0.25, 0.6) : 1 - Seg(x, 0.3, 0.7);
        var flare = on ? Math.Sin(Math.PI * Seg(x, 0.52, 0.8)) : 0;
        var bloom = on ? 0 : Math.Sin(Math.PI * Seg(x, 0.6, 1));
        var embers = on ? Seg(x, 0.62, 1) : 0;

        // 1. Sun and horizon, seen through the gap.
        var horizon = h * 0.6;
        var R = Math.Min(w, h) * 0.15;
        var sunY = horizon - R * 0.55 + R * 1.7 * sunP;
        var sunA = veil * (1 - Seg(sunP, 0.85, 1));
        if (sunA > 0)
        {
            var c = new Point(w / 2, sunY);
            dc.DrawEllipse(Rad(c, R * 3, (Ember, 0.4 * sunA, 0.27), (Rose, 0.12 * sunA, 0.6), (Rose, 0, 1)), null, c, R * 3, R * 3);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, horizon)));
            var disc = new GeometryGroup { FillRule = FillRule.EvenOdd };
            disc.Children.Add(new EllipseGeometry(c, R, R));
            var bands = new[] { (0.12, 0.05), (0.36, 0.08), (0.6, 0.11), (0.84, 0.15) };
            for (var i = 0; i < bands.Length; i++)
            {
                var drift = Math.Sin(t * 2 + i) * R * 0.02;
                disc.Children.Add(new RectangleGeometry(new Rect(c.X - R * 1.2, c.Y + R * bands[i].Item1 + drift, R * 2.4, R * bands[i].Item2)));
            }
            dc.DrawGeometry(Lin(new(c.X, c.Y - R), new(c.X, c.Y + R), (Sun, sunA), (Ember, sunA), (Rose, sunA)), null, disc);
            dc.Pop();
            dc.DrawRectangle(Lin(new(0, horizon - 30), new(0, horizon + 30), (Ember, 0), (Ember, 0.22 * sunA), (Ember, 0)), null, new Rect(0, horizon - 30, w, 60));
            dc.DrawRectangle(Solid(Sun, 0.7 * sunA), null, new Rect(0, horizon - 0.75, w, 1.5));
        }

        // 2. Velvet curtains with a starry lining and a wavy, swinging hem.
        var half = w / 2 * close;
        if (half > 0.5 && veil > 0)
        {
            var sway = 10 + 30 * Math.Sin(Math.PI * Math.Min(close, 1 - close) * 2);
            double Wave(double y, double ph)
            {
                var hang = 0.25 + 0.75 * y / h;
                return sway * hang * (0.6 * Math.Sin(y / 90 + t * 5 + ph) + 0.4 * Math.Sin(y / 37 - t * 3.3 + ph * 2));
            }
            var ys = new List<double>();
            for (var y = 0.0; y < h; y += 8) ys.Add(y);
            ys.Add(h);
            foreach (var left in new[] { true, false })
            {
                var ph = left ? 0.0 : 1.7;
                double Ex(double y) => left ? half + Wave(y, ph) : w - half - Wave(y, ph);
                var edgePts = ys.Select(y => new Point(Ex(y), y)).ToList();
                var outside = left ? -60 : w + 60;
                var cloth = Poly(edgePts.Append(new(outside, h)).Append(new(outside, 0)), true);
                var edge = Poly(edgePts, false);

                dc.DrawGeometry(Lin(new(0, 0), new(0, h), (Night, 0.88 * veil), (Indigo, 0.8 * veil), (Rose, 0.72 * veil)), null, cloth);
                dc.PushClip(cloth);
                const int n = 9;
                var pleat = half / n;
                var pleatPen = new Pen(Solid(Colors.Black, 0.2 * veil), Math.Max(pleat * 0.45, 2));
                for (var i = 0; i < n; i++)
                {
                    var f = i / (double)n;
                    dc.DrawGeometry(null, pleatPen, Poly(ys.Select(y => new Point(left ? pleat * i + Wave(y, ph) * f : w - pleat * i - Wave(y, ph) * f, y)), false));
                }
                // Stars woven into the upper lining, pinned to the cloth so they travel with it.
                if (stars > 0)
                    for (var i = 0; i < 70; i++)
                    {
                        double u = Rnd(i, left ? 1 : 2), v = Rnd(i, 3);
                        var y = v * v * h * 0.62;
                        var px = left ? u * half + Wave(y, ph) * u : w - u * half - Wave(y, ph) * u;
                        var tw = 0.55 + 0.45 * Math.Sin(t * (3 + Rnd(i, 4) * 4) + i);
                        var a = stars * veil * tw * (0.5 + 0.5 * Rnd(i, 5));
                        var r = 0.7 + 1.6 * Rnd(i, 6);
                        var p = new Point(px, y);
                        dc.DrawEllipse(Solid(Colors.White, a), null, p, r, r);
                        if (Rnd(i, 7) > 0.85) dc.DrawEllipse(Rad(p, r * 4, (Sun, a * 0.5, 0), (Sun, 0, 1)), null, p, r * 4, r * 4);
                    }
                dc.Pop();
                dc.DrawGeometry(null, new Pen(Solid(Ember, 0.16 * veil), 40), edge);
                dc.DrawGeometry(null, new Pen(Solid(Ember, 0.3 * veil), 12), edge);
                dc.DrawGeometry(null, new Pen(Solid(Sun, 0.85 * veil), 2), edge);
            }
        }

        // 3. The seam flares where the curtains meet.
        if (flare > 0)
        {
            var fw = 220 * flare;
            dc.DrawRectangle(Lin(new(w / 2 - fw, 0), new(w / 2 + fw, 0), (Ember, 0), (Ember, 0.45 * flare), (Sun, 0.7 * flare), (Ember, 0.45 * flare), (Ember, 0)),
                null, new Rect(w / 2 - fw, 0, fw * 2, h));
        }

        // 4. The curtain burns away into embers that drift up.
        if (embers is > 0 and < 1)
            for (var i = 0; i < 90; i++)
            {
                var life = Seg(embers, Rnd(i, 8) * 0.35, 1);
                if (life is <= 0 or >= 1) continue;
                var x0 = w / 2 + (Rnd(i, 9) - 0.5) * w * (0.15 + 0.85 * Rnd(i, 10));
                var y0 = h * (0.35 + 0.65 * Rnd(i, 11));
                var p = new Point(x0 + Math.Sin(t * 2 + i) * 18 * life, y0 - life * h * (0.25 + 0.3 * Rnd(i, 12)));
                var r = 1.2 + 2.6 * Rnd(i, 13) * (1 - life);
                var a = Math.Sin(Math.PI * life);
                dc.DrawEllipse(Rad(p, r * 4, (Ember, 0.35 * a, 0), (Ember, 0, 1)), null, p, r * 4, r * 4);
                dc.DrawEllipse(Solid(Sun, 0.9 * a), null, p, r, r);
            }

        // 5. Dawn bloom as the sun clears the curtain.
        if (bloom > 0)
        {
            var c = new Point(w / 2, horizon - R);
            var big = Math.Max(w, h) * 0.7;
            dc.DrawRectangle(Rad(c, big, (Sun, 0.35 * bloom, 0), (Ember, 0.12 * bloom, 0.5), (Ember, 0, 1)), null, new Rect(0, 0, w, h));
        }
    }
}

/// Click-through windows over every screen that play one Sundown, then close.
static class ScreenSundown
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    static List<Window> windows = new();

    public static void Play(bool on)
    {
        if (!SystemParameters.ClientAreaAnimation) return; // Windows "Animation effects" off: skip the show
        foreach (var w in windows) w.Close();
        windows = System.Windows.Forms.Screen.AllScreens.Select(s =>
        {
            var scene = new SundownScene(on);
            var w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ShowInTaskbar = false, Topmost = true, ShowActivated = false, Focusable = false,
                ResizeMode = ResizeMode.NoResize, Content = scene, Left = -32000, Top = -32000, Width = 1, Height = 1,
            };
            w.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                // WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE: clicks pass through, no Alt-Tab, no focus.
                SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x20 | 0x80 | 0x08000000);
                var b = s.Bounds; // physical pixels; WPF converts to this monitor's DPI
                SetWindowPos(hwnd, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, 0x0010 /* SWP_NOACTIVATE */);
            };
            w.Show();
            return w;
        }).ToList();
        var mine = windows;
        var done = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(SundownScene.Duration + 0.05) };
        done.Tick += (_, _) => { done.Stop(); foreach (var w in mine) w.Close(); if (windows == mine) windows = new(); };
        done.Start();
    }
}
