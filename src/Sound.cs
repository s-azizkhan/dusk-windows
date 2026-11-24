using System.Media;

namespace Dusk;

/// Tiny synthesized UI clicks, same recipes as the Mac app. No audio files.
static class Sound
{
    static readonly Dictionary<string, SoundPlayer> cache = new();
    static readonly Random rng = new(7);

    /// Detent tick; pitch 0...1 maps low to high.
    public static void Tick(double pitch = 0.5) => Play(1500 + 1500 * pitch, 7, 0.22, 0.25);
    /// Firmer click when snapping to a preset or step.
    public static void Snap() => Play(2600, 12, 0.4, 0.2);
    /// Soft "tock" for button presses.
    public static void Press() => Play(520, 22, 0.45, 0.35);
    /// Rising chirp for on, falling for off.
    public static void Toggle(bool on) => Play(on ? 760 : 1250, 55, 0.35, 0.05, on ? 520 : -520);

    static void Play(double freq, double ms, double gain, double noise, double glide = 0)
    {
        if (!Program.S.Sounds) return;
        var key = $"{(int)freq}-{(int)ms}-{(int)glide}";
        if (!cache.TryGetValue(key, out var p))
        {
            p = new SoundPlayer(new MemoryStream(Wav(freq, ms, gain, noise, glide)));
            p.Load();
            cache[key] = p;
        }
        try { p.Play(); } catch (Exception) { } // no audio device
    }

    // 16-bit mono PCM WAV, 44.1 kHz. Matches the Mac synth: sine + decaying noise click, fast attack, exp decay.
    // Short lead-in of silence so drivers that clip the first few ms don't eat the click.
    internal static byte[] Wav(double freq, double ms, double gain, double noise, double glide)
    {
        const int sr = 44100, lead = 64;
        var n = (int)(sr * ms / 1000);
        var dur = ms / 1000; var decay = 6 / dur; var phase = 0.0;
        var data = new short[lead + n];
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / sr;
            var env = Math.Min(t / 0.0008, 1) * Math.Exp(-t * decay);
            phase += 2 * Math.PI * (freq + glide * t / dur) / sr;
            var click = (rng.NextDouble() * 2 - 1) * noise * Math.Exp(-t * decay * 5);
            data[lead + i] = (short)Math.Clamp((Math.Sin(phase) * (1 - noise) + click) * env * gain * 32767, -32768, 32767);
        }
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write("RIFF"u8); w.Write(36 + data.Length * 2); w.Write("WAVEfmt "u8);
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data.Length * 2);
        foreach (var s in data) w.Write(s);
        return m.ToArray();
    }
}
