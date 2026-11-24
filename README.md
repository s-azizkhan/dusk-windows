<p align="center"><img src="src/Dusk.ico" width="96" alt="Dusk app icon: a sunset sun cut by horizontal bands"></p>

<h1 align="center">Dusk for Windows</h1>

<p align="center"><b>Warm screen. Calm eyes.</b><br>
Free, open-source tray app that warms your screen down to 500K, dims it to 5% and holds the backlight at full power to cut PWM flicker.</p>

---

Dusk for Windows is the Windows version of [Dusk for Mac](https://github.com/s-azizkhan/dusk-mac). No account, no analytics, no network calls.

**Status: early preview (0.1.0).** Tested on a Windows 11 VM, not yet on real hardware. Please report what works and what doesn't.

## Features

- Warmth from 6500K down to 500K, with seven presets: Energy 6000K, Daylight 5000K, Evening 3400K, Night 2700K, Late 1900K, Deep 1200K, Midnight 500K
- Software dimming down to 5%
- Flicker-free mode (on by default): laptop backlight and DDC/CI monitors held at 100%, dimming done in software
- Night schedule, launch at login, re-applies after sleep, unlock and display changes
- Same panel as the Mac app: warmth dial, brightness bars, preset chips, settings page, soft click sounds

## Install

1. Download `Dusk.exe` from the latest release.
2. Run it. Windows SmartScreen may warn because the exe isn't signed yet: click **More info**, then **Run anyway**.
3. Click the sun icon in the tray. It may start in the hidden icons area (the ^ arrow); drag it onto the taskbar to keep it handy.

Using the panel:
- Drag the dial ring to set warmth. It snaps to presets.
- Click the center of the dial to turn Dusk on or off.
- Click or drag the bars to dim.
- The sliders button opens Settings.
- Right-click the tray icon for a quick menu.

Needs Windows 10 or 11, 64-bit. Nothing else to install.

### Unlock the full range (once)

Windows refuses gamma ramps that stray far from normal, so warm settings below roughly 3000K and deep dimming fail out of the box. If that happens Dusk shows a warning and an **Unlock warmer and dimmer** button. It asks for admin once and sets:

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
- **Panel:** WPF, drawn in code to match the Mac SwiftUI views (same sizes, colors, springs and sound recipes). Instrument Serif and Geist are bundled (SIL OFL 1.1). Geist's Medium and SemiBold weights are saved as separate files because WPF can't pick weights from a variable font.
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
  - responds to real mouse clicks: a preset chip, the power switch, the brightness bars, the dial ring, the dial center, and the Sounds switch on the settings page

  Each run also saves screenshots of both pages so the layout can be checked by eye.

  On that runner Windows accepted 3400K at 100% but silently rejected 3400K at 80% and 1200K at 30%, even right after setting `GdiIcmGammaRange`. The read-back caught each rejection, and flicker-free correctly refused to pin. The runner has a virtual GPU with no backlight, so real brightness control still needs a real PC.

## Differences from the Mac app

- No haptics. Windows has no trackpad haptics API.
- No anti-dithering row (see below).
- The panel background is solid, not frosted. macOS vibrancy has no direct WPF equivalent.
- Windows only: a warning and an Unlock button on the main page when Windows blocks a setting, plus a right-click tray menu.
- Small motion details are simplified: digits don't roll, the switch icon doesn't morph, and the chip highlight doesn't slide between presets.
- The exe is about 76 MB because it bundles .NET, WPF and WinForms. Nothing else needs installing.

## Known limitations

- **Not tested on real hardware yet.** The smoke test runs it on a Windows VM. Still needs a real PC:
  - gamma on NVIDIA, AMD and Intel drivers
  - whether the unlock needs a restart
  - the UAC prompt
  - WMI and DDC/CI brightness
  - sleep and display changes
  - scaling above 100%
  - sounds (the runner has no audio device)
- **No anti-dithering.** NVIDIA's dither control is only reachable through undocumented NVAPI calls (what [novideo_srgb](https://github.com/ledoge/novideo_srgb) uses), and AMD and Intel have no safe public path. Dusk skips it rather than poke the driver blind. novideo_srgb or ColorControl can do it on NVIDIA today.
- **HDR:** Windows ignores gamma ramps while HDR is on, and the read-back may not notice. Turn HDR off, and don't use flicker-free with HDR on, or the backlight can go to 100% with no dimming.
- **Driver caps:** some drivers refuse warm or dim settings past a point, even after unlocking (one report: nothing past Evening). Dusk then sets the warmest the driver accepts and says how far it got. Restart Windows once after unlocking before judging the cap.
- **Night light:** turn off Windows Night light so the two don't fight.
- Some monitors don't support DDC/CI, or have it turned off in their own menu. Flicker-free skips those.
- Some laptops reset brightness on their own (adaptive brightness, power plans). Turn off "Change brightness automatically" for flicker-free to hold.
- Flicker reduction isn't measured with an oscilloscope. A slow-motion phone video is a quick check.

## Credits

Built on ideas from [LightBulb](https://github.com/Tyrrrz/LightBulb), [Gammy](https://github.com/Fushko/gammy), [Twinkle Tray](https://github.com/xanderfrangos/twinkle-tray) and [novideo_srgb](https://github.com/ledoge/novideo_srgb).

## License

MIT © s-azizkhan

Built with love by [Aziz](https://justaziz.com?utm_source=dusk-windows).
