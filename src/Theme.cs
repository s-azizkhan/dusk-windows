using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Dusk;

/// Colors, fonts and glyphs shared by the panel. Mirrors the Mac app's SwiftUI values.
static class Theme
{
    public static readonly Color PanelBg = Rgb(0x1C1A24);
    public static readonly Color Sun = Color.FromRgb(255, 224, 138);
    public static readonly Color Ember = Color.FromRgb(255, 154, 77);
    public static readonly Color Rose = Color.FromRgb(242, 79, 122);
    public static readonly Color Crimson = Color.FromRgb(199, 31, 51);
    public static readonly Color Mint = Rgb(0x63E6E2), Orange = Rgb(0xFF9F0A), Pink = Rgb(0xFF375F);
    public static readonly Brush Secondary = new SolidColorBrush(A(Colors.White, 0.55)), Tertiary = new SolidColorBrush(A(Colors.White, 0.3));

    public static Color Rgb(int v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    public static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);
    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    public static Color Gray(Color c, double amount)
    {
        var l = (byte)(0.3 * c.R + 0.59 * c.G + 0.11 * c.B);
        return Mix(c, Color.FromArgb(c.A, l, l, l), amount);
    }

    /// The screen tint for a temperature, same curve the gamma ramp uses.
    public static Color Warm(double kelvin)
    {
        var (r, g, b) = Warmth.Rgb(kelvin);
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    // Fonts ship inside the exe and are unpacked once to %LOCALAPPDATA%\Dusk\fonts (SIL OFL 1.1).
    static Uri fontDir = new("file:///C:/");
    public static FontFamily Serif = new("Georgia"), Sans = new("Segoe UI"), Mono = new("Consolas");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static void LoadFonts()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dusk", "fonts");
            Directory.CreateDirectory(dir);
            var asm = typeof(Theme).Assembly;
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf")))
            {
                var path = Path.Combine(dir, name);
                using var s = asm.GetManifestResourceStream(name)!;
                if (File.Exists(path) && new FileInfo(path).Length == s.Length) continue;
                using var f = File.Create(path);
                s.CopyTo(f);
            }
            fontDir = new Uri(dir + Path.DirectorySeparatorChar);
            // Static weight files carry both a legacy family ("Geist Medium") and a typographic one ("Geist").
            Serif = new(fontDir, "./#Instrument Serif, Georgia");
            Sans = new(fontDir, "./#Geist, Segoe UI");
            Mono = new(fontDir, "./#Geist Mono, Consolas");
        }
        catch (Exception) { } // system fonts are fine if %LOCALAPPDATA% is unwritable
    }

    public static FontFamily SansW(int weight) => weight switch
    {
        >= 600 => new(fontDir, "./#Geist SemiBold, ./#Geist, Segoe UI"),
        >= 500 => new(fontDir, "./#Geist Medium, ./#Geist, Segoe UI"),
        _ => Sans,
    };
    public static FontFamily MonoW(int weight) => weight >= 500 ? new(fontDir, "./#Geist Mono Medium, ./#Geist Mono, Consolas") : Mono;
    public static FontWeight W(int weight) => FontWeight.FromOpenTypeWeight(weight);

    // Segoe Fluent Icons / MDL2 glyphs standing in for the Mac app's SF Symbols.
    public const string GSun = "\uE706", GSunLow = "\uEC8A", GMoon = "\uE708", GSliders = "\uE9E9", GClose = "\uE711",
        GPulse = "\uE9D9", GClock = "\uE917", GPower = "\uE7E8", GSpeaker = "\uE767", GHeart = "\uEB52",
        GWarn = "\uE7BA", GArrow = "\uE72A";

    /// SwiftUI-ish spring: overshoots a little and settles. Duration ~ response.
    public static IEasingFunction Bounce(double damping = 0.78) =>
        new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 + 8 * damping };

    public static void Animate(DependencyObject target, DependencyProperty p, double to, double ms = 380, IEasingFunction? ease = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (target is UIElement u) u.BeginAnimation(p, a);
        else if (target is Animatable an) an.BeginAnimation(p, a);
    }
}
