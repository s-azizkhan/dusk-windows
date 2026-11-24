<p align="center"><img src="src/Dusk.ico" width="96" alt="Dusk app icon: a sunset sun cut by horizontal bands"></p>

<h1 align="center">Dusk for Windows</h1>

<p align="center"><b>Warm screen. Calm eyes.</b><br>
Free, open-source tray app that warms your screen down to 500K, dims it to 5% and holds the backlight at full power to cut PWM flicker.</p>

---

Dusk for Windows is the Windows version of [Dusk for Mac](https://github.com/s-azizkhan/dusk-mac). No account, no analytics, no network calls.

**Status: early preview (0.1.0).** Built on a Mac and not yet run on real Windows hardware. Please report what works and what doesn't.

## Features

- Warmth from 6500K down to 500K, with seven presets: Energy 6000K, Daylight 5000K, Evening 3400K, Night 2700K, Late 1900K, Deep 1200K, Midnight 500K
- Software dimming down to 5%
- Flicker-free mode: laptop backlight and DDC/CI monitors held at 100%, dimming done in software
- Night schedule, start with Windows, re-applies after sleep, unlock and display changes

## Install

1. Download `Dusk.exe` from the latest release.
2. Run it. Windows SmartScreen may warn because the exe isn't signed yet: click **More info**, then **Run anyway**.
3. Click the sun icon in the tray (you may need to drag it out of the hidden icons area).

Needs Windows 10 or 11, 64-bit. Nothing else to install.

### Unlock the full range (once)

Windows refuses gamma ramps that stray far from normal, so warm settings below roughly 3000K and deep dimming fail out of the box. If that happens Dusk shows a warning and an **Unlock full range** button. It asks for admin once and sets:

```
HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\GdiIcmGammaRange = 256 (DWORD)
```

This is the same value [LightBulb](https://github.com/Tyrrrz/LightBulb) sets in its installer. If the warning stays after unlocking, restart Windows. To undo it, delete that value.

## Build from source

Needs the .NET 10 SDK. Works on Windows, macOS or Linux:

```sh
dotnet publish src -c Release -o dist
```

Output is a single self-contained `dist/Dusk.exe`. The color math has a quick check that runs on any OS:

```sh
dotnet run --project check
```

## How it works

- **Warmth and dimming:** `SetDeviceGammaRamp` on every display, then `GetDeviceGammaRamp` to confirm it applied. Windows returns success even when it silently rejects a ramp, so the read-back is the real test.
- **Flicker-free:** laptop panels through WMI `WmiMonitorBrightnessMethods`, external monitors through DDC/CI `SetMonitorBrightness`. Dusk only pins the backlight when the gamma read-back passed, so the screen never jumps to full brightness, and puts the old levels back when you turn it off or quit.
- **Settings:** `%APPDATA%\Dusk\settings.json`. While flicker-free is holding the backlight, the original levels are saved there too, so if Dusk crashes or Windows force-restarts, the next launch puts them back. Errors go to `%APPDATA%\Dusk\error.log`.
- **Calibration:** Dusk only resets gamma if it changed it, so ICC/DisplayCAL calibration stays put while Dusk is off. Start with Windows uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## How this build was validated

There was no Windows machine available, so:

- Every Win32 call was checked against Microsoft's own metadata (the CsWin32 generator): DLL, parameter types, BOOL returns, struct layout (`PHYSICAL_MONITOR` is 264 bytes, pack 1).
- It was built with all .NET analyzers on, including single-file, platform and interop rules. All Win32 DLLs load from System32 only.
- `System.Management` was checked in single-file mode. It loads `wminet_utils.dll` from the .NET Framework 4.8 that ships with Windows 10 and 11.
- An independent code review compared the code against Microsoft docs and LightBulb, Twinkle Tray and novideo_srgb. All 15 findings with evidence were fixed, including:
  - a start-with-Windows toggle that could never change
  - overlapping UI at 125% scaling and above
  - brightness lost after a crash
  - monitors mixed up on restore
  - a cancelled sign-out leaving a zombie process
- The color math has a test that runs anywhere (`dotnet run --project check`).

- A smoke test (`ci/smoke.ps1`) runs on a GitHub Windows 11 runner on every push. It checks that Dusk:
  - starts, stays running, opens its panel, and only runs one copy
  - survives garbage and out-of-range settings without writing errors
  - puts back levels left over from a crashed run
  - shows the blocked warning and Unlock button when Windows rejects a ramp

  On that runner Windows accepted 3400K at 100% but silently rejected 3400K at 80% and 1200K at 30%, even right after setting `GdiIcmGammaRange`. The read-back caught each rejection, and flicker-free correctly refused to pin. The runner has a virtual GPU with no backlight, so real brightness control still needs a real PC.

## Known limitations

- **Not tested on real hardware yet.** The smoke test runs it on a Windows VM. Still needs a real PC:
  - gamma on NVIDIA, AMD and Intel drivers
  - whether the unlock needs a restart
  - the UAC prompt
  - WMI and DDC/CI brightness
  - sleep and display changes
  - scaling above 100%
- **No anti-dithering.** NVIDIA's dither control is only reachable through undocumented NVAPI calls (what [novideo_srgb](https://github.com/ledoge/novideo_srgb) uses), and AMD and Intel have no safe public path. Dusk skips it rather than poke the driver blind. novideo_srgb or ColorControl can do it on NVIDIA today.
- **HDR:** Windows ignores gamma ramps while HDR is on, and the read-back may not notice. Turn HDR off, and don't use flicker-free with HDR on, or the backlight can go to 100% with no dimming.
- **Night light:** turn off Windows Night light so the two don't fight.
- Some monitors don't support DDC/CI, or have it turned off in their own menu. Flicker-free skips those.
- Some laptops reset brightness on their own (adaptive brightness, power plans). Turn off "Change brightness automatically" for flicker-free to hold.
- Flicker reduction isn't measured with an oscilloscope. A slow-motion phone video is a quick check.

## Credits

Built on ideas from [LightBulb](https://github.com/Tyrrrz/LightBulb), [Gammy](https://github.com/Fushko/gammy), [Twinkle Tray](https://github.com/xanderfrangos/twinkle-tray) and [novideo_srgb](https://github.com/ledoge/novideo_srgb).

## License

MIT © s-azizkhan

Built with love by [Aziz](https://justaziz.com?utm_source=dusk-windows).
