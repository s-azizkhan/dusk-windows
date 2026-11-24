namespace Dusk;

public static class Warmth
{
    // Tanner Helland approximation, extended below 1000K toward deep red. Same curve as the Mac app.
    public static (double r, double g, double b) Rgb(double kelvin)
    {
        if (kelvin >= 6500) return (1, 1, 1); // neutral, so "off" and quit restore an identity ramp
        var t = Math.Max(kelvin, 1000) / 100;
        double r = 1, g, b;
        if (t > 66) r = 1.2929 * Math.Pow(t - 60, -0.1332);
        g = t <= 66 ? 0.39008 * Math.Log(t) - 0.63184 : 1.12989 * Math.Pow(t - 60, -0.0755);
        if (t >= 66) b = 1; else if (t <= 19) b = 0; else b = 0.54320 * Math.Log(t - 10) - 1.19625;
        if (kelvin < 1000)
        {
            var f = Math.Max((kelvin - 500) / 500, 0);
            g *= 0.4 + 0.6 * f;
            b *= f;
        }
        return (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
    }

    // GDI ramp: 3 x 256 WORDs, red then green then blue.
    public static ushort[] Ramp(double kelvin, double dim)
    {
        var (r, g, b) = Rgb(kelvin);
        var ramp = new ushort[768];
        for (var i = 0; i < 256; i++)
        {
            ramp[i] = (ushort)Math.Round(i * 257 * r * dim);
            ramp[256 + i] = (ushort)Math.Round(i * 257 * g * dim);
            ramp[512 + i] = (ushort)Math.Round(i * 257 * b * dim);
        }
        return ramp;
    }

    // t = 0 gives the neutral ramp, t = 1 gives the full ramp.
    public static ushort[] Blend(ushort[] full, double t)
    {
        var r = new ushort[768];
        for (var i = 0; i < 768; i++)
        {
            var id = (i % 256) * 257;
            r[i] = (ushort)Math.Round(id + t * (full[i] - id));
        }
        return r;
    }

    // Drivers round ramps, so allow a small slack per entry.
    public static bool Matches(ushort[] want, ushort[] got)
    {
        for (var i = 0; i < 768; i++)
            if (Math.Abs(want[i] - got[i]) > 512) return false;
        return true;
    }
}
