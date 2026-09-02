# Captures the virtual desktop and crops to a named process's main window,
# with a margin so popups that sit just outside it are still caught.
#
# Tooltips and dropdowns are separate top-level windows, so a per-window
# capture misses them; only a desktop capture shows where they actually land.
param(
    [string]$Process = "Merpeeps.App",
    [string]$Out = "$env:TEMP\merpeeps-screen.png",
    [int]$DelaySeconds = 0,
    [int]$Margin = 220)

if ($DelaySeconds -gt 0) { Start-Sleep -Seconds $DelaySeconds }

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class NativeRect {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@

# Without this, GetWindowRect and SystemInformation.VirtualScreen report
# virtualised logical coordinates while CopyFromScreen captures real pixels. The
# crop rectangle then lands in the wrong place and at the wrong scale — the
# symptom is a screenshot zoomed to about 150% and clipped on two sides.
[void][NativeRect]::SetProcessDPIAware()

$bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$gfx.Dispose()

$proc = Get-Process $Process -ErrorAction SilentlyContinue | Select-Object -First 1
if ($proc -and $proc.MainWindowHandle -ne 0) {
    $r = New-Object NativeRect+R
    [void][NativeRect]::GetWindowRect($proc.MainWindowHandle, [ref]$r)

    # Screen coordinates are offset from bitmap coordinates by the virtual origin,
    # which is negative whenever a monitor sits left of the primary one.
    $x = [Math]::Max(0, $r.L - $bounds.X - $Margin)
    $y = [Math]::Max(0, $r.T - $bounds.Y - $Margin)
    $w = [Math]::Min($bmp.Width - $x, ($r.Rt - $r.L) + $Margin * 2)
    $h = [Math]::Min($bmp.Height - $y, ($r.B - $r.T) + $Margin * 2)

    $crop = New-Object System.Drawing.Rectangle $x, $y, $w, $h
    $sub = $bmp.Clone($crop, $bmp.PixelFormat)
    $sub.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $sub.Dispose()
    Write-Output "cropped to $Process at screen ($($r.L),$($r.T)) -> ${w}x${h}"
}
else {
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "process '$Process' not found; saved full desktop"
}

$bmp.Dispose()
