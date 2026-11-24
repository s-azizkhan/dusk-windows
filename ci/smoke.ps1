# Runs Dusk.exe on a GitHub Windows runner: starts, survives, single instance, bad settings, crash restore, gamma read-back.
# The runner has no real GPU or backlight, so gamma/WMI/DDC results are informational, not pass/fail.
$ErrorActionPreference = 'Stop'
$out = (New-Item -ItemType Directory -Force smoke).FullName
$dir = Join-Path $env:APPDATA 'Dusk'
$exe = Resolve-Path dist/Dusk.exe
$script:fail = $false

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing.Primitives @'
using System; using System.Runtime.InteropServices;
public static class G {
  [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateDC(string d, string dev, string o, IntPtr m);
  [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr h);
  [DllImport("gdi32.dll")] static extern bool GetDeviceGammaRamp(IntPtr h, ushort[] r);
  public static string Read() {
    var s = "";
    foreach (var sc in System.Windows.Forms.Screen.AllScreens) {
      var dc = CreateDC(sc.DeviceName, null, null, IntPtr.Zero); var r = new ushort[768];
      var ok = dc != IntPtr.Zero && GetDeviceGammaRamp(dc, r); DeleteDC(dc);
      s += sc.DeviceName + " read=" + ok + " R255=" + r[255] + " G255=" + r[511] + " B255=" + r[767] + "; ";
    }
    return s;
  }
}
'@

Add-Type @'
using System; using System.Runtime.InteropServices; using System.Threading;
public static class M {
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  public static void Click(int x, int y) { SetCursorPos(x, y); Thread.Sleep(120); mouse_event(2, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(80); mouse_event(4, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(400); }
}
'@

function Check($name, $cond) { if ($cond) { "PASS  $name" } else { "FAIL  $name"; $script:fail = $true } }
function Info($name, $val) { "INFO  ${name}: $val" }
function Shot($name) {
  try {
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    [System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $out "$name.png"))
  } catch { Info 'screenshot' "failed: $_" }
}
function Seed($json) { New-Item -ItemType Directory -Force $dir | Out-Null; Set-Content (Join-Path $dir 'settings.json') $json }
function Launch([string[]]$a = @()) { $p = Start-Process $exe -ArgumentList $a -PassThru; Start-Sleep 8; $p }
function Stop($p) { if (-not $p.HasExited) { Stop-Process $p -Force; Start-Sleep 1 } }
function NoErrors($tag) {
  $log = Join-Path $dir 'error.log'
  if (Test-Path $log) { Copy-Item $log (Join-Path $out "error-$tag.log") }
  Check "$tag : no error.log" (-not (Test-Path $log))
}

Info 'session' "$([System.Diagnostics.Process]::GetCurrentProcess().SessionId) interactive=$([Environment]::UserInteractive)"
Info 'screens' (([System.Windows.Forms.Screen]::AllScreens | ForEach-Object { "$($_.DeviceName) $($_.Bounds)" }) -join ', ')
Info 'gpu' ((Get-CimInstance Win32_VideoController).Name -join ', ')
Info 'gamma before' ([G]::Read())

# 1. Normal start with flicker-free on (exercises WMI + DDC paths on hardware that has neither)
Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
Seed '{"Enabled":true,"Kelvin":3400,"Brightness":0.8,"FlickerFree":true}'
$p = Launch @('--open')
Check 'start: still running after 8s' (-not $p.HasExited)
Shot 'popup'
Info 'gamma at 3400K 80%' ([G]::Read())
$s = Get-Content (Join-Path $dir 'settings.json') -Raw | ConvertFrom-Json
Check 'start: settings.json is valid' ($s.Kelvin -eq 3400)
Info 'settings after start' (Get-Content (Join-Path $dir 'settings.json') -Raw)

# 2. Single instance
$p2 = Start-Process $exe -PassThru; Start-Sleep 4
Check 'second launch exits' $p2.HasExited
NoErrors 'start'
Stop $p

# 3. Unlocked gamma range (runner is already admin, so set the value the Unlock button sets)
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM" /v GdiIcmGammaRange /t REG_DWORD /d 256 /f | Out-Null
Seed '{"Enabled":true,"Kelvin":1200,"Brightness":0.3}'
$p = Launch
Check 'unlocked: still running' (-not $p.HasExited)
Info 'gamma at 1200K 30% unlocked' ([G]::Read())
NoErrors 'unlocked'
Stop $p

# 4. Corrupt and out-of-range settings must not crash
Seed '{"Enabled":true,"Kelvin":-5,"Brightness":0,"StartHour":99,"EndHour":-3,"ScheduleOn":true}'
$p = Launch @('--open')
Check 'bad settings: still running' (-not $p.HasExited)
NoErrors 'bad-settings'
Stop $p
Seed 'this is not json'
$p = Launch
Check 'garbage settings: still running' (-not $p.HasExited)
NoErrors 'garbage'
Stop $p

# 5. Left-over pin from a crashed run gets restored and cleared
Seed '{"Enabled":false,"Pinned":{"Laptop":null,"External":{"MONITOR\\FAKE\\0001|Generic PnP Monitor":40}}}'
$p = Launch
Check 'crash restore: still running' (-not $p.HasExited)
$s = Get-Content (Join-Path $dir 'settings.json') -Raw | ConvertFrom-Json
Check 'crash restore: Pinned cleared' ($null -eq $s.Pinned)
NoErrors 'crash-restore'
Stop $p

# 6. Screenshots of both pages in a state Windows accepts (no warning)
Seed '{"Enabled":true,"Kelvin":3400,"Brightness":1}'
$p = Launch @('--open'); Shot 'main'; NoErrors 'main'; Stop $p
$p = Launch @('--settings'); Start-Sleep 1; Shot 'settings'; NoErrors 'settings'; Stop $p
Seed '{"Enabled":false,"Kelvin":1900,"Brightness":0.6}'
$p = Launch @('--open'); Shot 'paused'; NoErrors 'paused'; Stop $p

# 7. Real mouse input on the panel (coordinates from the 1024x768 runner layout at 100% scaling)
function Saved { Get-Content (Join-Path $dir 'settings.json') -Raw | ConvertFrom-Json }
function Fresh { Seed '{"Enabled":true,"Kelvin":3400,"Brightness":1}'; Launch @('--open') }
$p = Fresh; [M]::Click(794, 606); Start-Sleep 1
Check 'click Night chip sets 2700K' ((Saved).Kelvin -eq 2700); Shot 'after-chip'; Stop $p
$p = Fresh; [M]::Click(976, 203); Start-Sleep 1
Check 'click power switch turns Dusk off' ((Saved).Enabled -eq $false); Shot 'after-power'; Stop $p
$p = Fresh; [M]::Click(839, 535); Start-Sleep 1
$b = (Saved).Brightness; Check "click middle of bars sets about 50% (got $b)" ($b -ge 0.4 -and $b -le 0.6); Stop $p
$p = Fresh; [M]::Click(768, 344); Start-Sleep 1
$k = (Saved).Kelvin; Check "click left of dial ring sets about 5300K (got $k)" ($k -ge 5100 -and $k -le 5500); Shot 'after-dial'; Stop $p
$p = Fresh; [M]::Click(854, 344); Start-Sleep 1
Check 'click dial center toggles off' ((Saved).Enabled -eq $false); Stop $p
$p = Fresh; [M]::Click(931, 203); Start-Sleep 1; [M]::Click(962, 498); Start-Sleep 1
Check 'settings page: Sounds switch turns sounds off' ((Saved).Sounds -eq $false); Shot 'after-sounds'; Stop $p

Info 'gamma at end' ([G]::Read())
if ($script:fail) { throw 'Smoke test failed' }
