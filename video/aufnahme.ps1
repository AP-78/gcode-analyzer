param(
    [Parameter(Mandatory)][string]$Gcode,
    [string]$Out = "$PSScriptRoot\out\bilder",
    [string]$Exe = "$PSScriptRoot\..\publish\GcodeAnalyzer.exe",
    [int]$Step = 2,
    # Inhaltsgröße des Fensters; 1280x720 wird im Video auf 1920x1080 hochskaliert (größere Schrift).
    [int]$Width = 1280,
    [int]$Height = 720
)
# Nimmt alle Bilder fürs Video auf: startet die App in der Größe $Width x $Height, steuert sie per UI-Automation
# und rendert das Fenster per PrintWindow (ohne Fokuswechsel). Schreibt PNGs und rects.json nach $Out.
# Die Favoriten des Nutzers werden vorher gesichert und danach wiederhergestellt.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  public struct RECT { public int L, T, R, B; }
  public struct POINT { public int X, Y; } }
'@
[W]::SetProcessDPIAware() | Out-Null
$A = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]::Descendants

New-Item -ItemType Directory -Force $Out | Out-Null
Remove-Item "$Out\*.png" -ErrorAction SilentlyContinue
$favFile = "$env:APPDATA\GcodeAnalyzer\favoriten.json"
$favBackup = "$env:TEMP\favoriten-backup.json"
$hadFav = Test-Path $favFile
# Für die Aufnahme gelten die Standard-Favoriten.
if ($hadFav) { Copy-Item $favFile $favBackup -Force; Remove-Item $favFile }
# Neutraler Pfad, weil die Statuszeile den vollen Dateipfad zeigt.
$neutral = 'C:\gcode'
New-Item -ItemType Directory -Force $neutral | Out-Null
$file = Join-Path $neutral (Split-Path $Gcode -Leaf)
Copy-Item $Gcode $file -Force

$p = Start-Process (Resolve-Path $Exe) -ArgumentList "`"$file`"" -PassThru
try {
    function Win { $script:win = $A::FromHandle($p.MainWindowHandle) }
    function Find([string]$name, [switch]$Side) {
        $all = $win.FindAll($Tree, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)))
        $hits = @($all | Where-Object { -not $Side -or $_.Current.BoundingRectangle.X -gt $winRect.L + $Width - 400 })
        $hits | Sort-Object { $_.Current.BoundingRectangle.Y } | Select-Object -First 1
    }
    function Capture([string]$name) {
        Start-Sleep -Milliseconds 150
        # Ganzes Fenster rendern, dann nur den Inhalt ohne Titelleiste ($Width x $Height) speichern.
        $bmp = New-Object System.Drawing.Bitmap ($wr.R - $wr.L), ($wr.B - $wr.T)
        $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
        [W]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null
        $g.ReleaseHdc($hdc); $g.Dispose()
        $crop = $bmp.Clone((New-Object System.Drawing.Rectangle ($winRect.L - $wr.L), ($winRect.T - $wr.T), $Width, $Height), $bmp.PixelFormat)
        $crop.Save("$Out\$name.png"); $crop.Dispose(); $bmp.Dispose()
    }
    function Rel($r) { @([int]($r.X - $winRect.L), [int]($r.Y - $winRect.T), [int]$r.Width, [int]$r.Height) }
    function SetLayer([int]$n) { $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue($n) }
    function ScrollTo([string]$name) {
        $sp = $pane.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        $sp.SetScrollPercent(-1, 0); Start-Sleep -Milliseconds 300
        if (-not $name) { return }
        $pr = $pane.Current.BoundingRectangle
        $offset = (Find $name -Side).Current.BoundingRectangle.Y - $pr.Y - 8
        $max = $pr.Height * 100 / $sp.Current.VerticalViewSize - $pr.Height
        $sp.SetScrollPercent(-1, [Math]::Min(100, [Math]::Max(0, $offset / $max * 100))); Start-Sleep -Milliseconds 300
    }

    # Warten, bis die Datei geladen ist (Layer-Regler erscheint erst danach).
    $slider = $null
    for ($i = 0; $i -lt 120 -and -not $slider; $i++) {
        Start-Sleep -Milliseconds 500; $p.Refresh()
        if ($p.MainWindowHandle -ne 0) {
            Win
            $slider = $win.FindFirst($Tree, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Slider)))
        }
    }
    if (-not $slider) { throw 'App hat die Datei nicht geladen.' }
    # Fenster so groß machen, dass der Inhalt (ohne Titelleiste) genau $Width x $Height hat.
    $h = $p.MainWindowHandle
    [W]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, $Width, $Height, 0x14) | Out-Null  # NOZORDER|NOACTIVATE
    Start-Sleep 1
    $c = New-Object W+RECT; [W]::GetClientRect($h, [ref]$c) | Out-Null
    [W]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 2 * $Width - $c.R, 2 * $Height - $c.B, 0x14) | Out-Null
    Start-Sleep 2
    $wr = New-Object W+RECT; [W]::GetWindowRect($h, [ref]$wr) | Out-Null
    $pt = New-Object W+POINT; [W]::ClientToScreen($h, [ref]$pt) | Out-Null
    # $winRect = Ursprung des Fensterinhalts; alle Rechtecke in rects.json beziehen sich darauf.
    $winRect = @{ L = $pt.X; T = $pt.Y }
    $pane = @($win.FindAll($Tree, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Pane' -and $_.Current.BoundingRectangle.X -gt $winRect.L + $Width - 400 -and
            $_.GetSupportedPatterns() -contains [System.Windows.Automation.ScrollPattern]::Pattern })[0]
    $max = [int]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Maximum
    $rects = @{}
    function FavRect {
        $head = (Find 'Favoriten' -Side).Current.BoundingRectangle
        $first = @('Hinweise', 'Aktueller Layer' | ForEach-Object { Find $_ -Side } | Where-Object { $_ } |
            ForEach-Object { $_.Current.BoundingRectangle.Y } | Sort-Object)[0]
        $pr = $pane.Current.BoundingRectangle
        @([int]($pr.X - $winRect.L), [int]($head.Y - $winRect.T - 12), [int]($pr.Width - 16), [int]($first - $head.Y + 4))
    }

    # 1 Übersicht
    SetLayer $max; ScrollTo ''
    Capture '01_uebersicht'
    $rects.favoriten = FavRect

    # 2 Favorit anheften
    (Find 'Favorit Geschätzte Druckzeit' -Side).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 500
    Capture '02_favorit'
    $rects.favoriten_neu = FavRect

    # 3 Layer für Layer
    for ($l = 1; $l -le $max; $l += $Step) { SetLayer $l; Capture ('03_layer_{0:000}' -f $l) }
    SetLayer $max; Capture ('03_layer_{0:000}' -f $max)

    # 4 Wände und Schalen
    ScrollTo 'Wände'
    Capture '04_waende'
    $top = (Find 'Wände' -Side).Current.BoundingRectangle; $bottom = (Find 'Infill' -Side).Current.BoundingRectangle
    $rects.waende = @([int]($top.X - $winRect.L - 4), [int]($top.Y - $winRect.T - 4), [int]($pane.Current.BoundingRectangle.Right - $top.X - 16), [int]($bottom.Y - $top.Y))

    # 5 Infill und Support
    ScrollTo 'Infill'
    Capture '05_auswertung'
    $top = (Find 'Infill' -Side).Current.BoundingRectangle; $bottom = (Find 'Datei' -Side).Current.BoundingRectangle
    $rects.infill_support = @([int]($top.X - $winRect.L - 4), [int]($top.Y - $winRect.T - 4), [int]($pane.Current.BoundingRectangle.Right - $top.X - 16), [int]($bottom.Y - $top.Y))

    # 6 Einfärbung
    ScrollTo ''
    $box = Find 'Einfärbung'
    $rects.einfaerbung = Rel $box.Current.BoundingRectangle
    $modes = 'Pfad-Kategorie', 'Geschwindigkeit', 'Volumenstrom', 'Layerzeit'
    for ($i = 0; $i -lt $modes.Count; $i++) {
        $ec = $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $ec.Expand(); Start-Sleep -Milliseconds 400
        $item = $box.FindAll($Tree, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))) |
            Where-Object { $_.Current.Name -like "*$($modes[$i])*" } | Select-Object -First 1
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $ec.Collapse(); Start-Sleep 2
        Capture ('06_modus_{0}' -f ($i + 1))
    }

    # 7 Zeit pro Layer: Sprung auf einen Layer wie nach einem Klick ins Diagramm
    $item = $null; $ec = $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern); $ec.Expand(); Start-Sleep -Milliseconds 400
    ($box.FindAll($Tree, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))) |
        Select-Object -First 1).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $ec.Collapse(); Start-Sleep 2
    $rects.diagramm = Rel (Find 'Zeit pro Layer (Wurzelskala)').Current.BoundingRectangle
    $chartLayer = [int]($max * 0.7)
    SetLayer $chartLayer
    Capture '07_diagramm'
    $rects.diagramm_layer = $chartLayer
    $rects.layer_max = $max

    $rects | ConvertTo-Json | Set-Content "$Out\rects.json" -Encoding utf8
    "$((Get-ChildItem "$Out\*.png").Count) Bilder in $Out"
}
finally {
    if (-not $p.HasExited) { Stop-Process $p.Id }
    if ($hadFav) { Move-Item $favBackup $favFile -Force } elseif (Test-Path $favFile) { Remove-Item $favFile }
    Remove-Item $neutral -Recurse -Force
}
