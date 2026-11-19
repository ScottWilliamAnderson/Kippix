# Draws the Links panel with made-up sample links to a PNG, without Playnite and without showing a window: a look at the
# layout while working on it. Nothing here reads the real list (the sample store lives in a temp folder and is removed).
#   powershell -NoProfile -STA -ExecutionPolicy Bypass -File tests\preview.ps1 -Out panel.png [-View list|rename|confirm|empty]
param([Parameter(Mandatory)][string]$Out, [string]$View = "list", [string]$TempRoot = $env:TEMP, [double]$Scale = 1.5)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
[void][Reflection.Assembly]::LoadFrom("$env:LOCALAPPDATA\Playnite\Playnite.SDK.dll")
$asm = [Reflection.Assembly]::LoadFrom((Join-Path $root "build\KippixLinks.dll"))
$dir = Join-Path $TempRoot ("kxlinks-preview-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $dir | Out-Null
try {
    $store = New-Object KippixLinks.LinkStore $dir
    $first = $null
    if ($View -ne "empty") {
        $samples = @(
            @("https://www.example.com/shows/the-long-road", "The Long Road | Example TV"),
            @("https://www.netflix.com/title/00000000", "A Made-Up Series | Netflix"),
            @("https://www.bbc.co.uk/iplayer/episode/x0000000", $null),
            @("https://www.primevideo.com/detail/0000000000", "Prime Video: Sample Film With A Rather Long Title That Goes On"),
            @("https://www.disneyplus.com/series/sample/0000", $null))
        foreach ($s in $samples) {
            $existed = $false
            $it = $store.AddOrTouch($s[0], [ref]$existed)
            if ($s[1]) { $store.SetTitle($it, $s[1]) }
            $first = $it
        }
        $store.MarkOpened($first)
    }
    $flags = [Reflection.BindingFlags]"Instance,Public,NonPublic"
    $type = $asm.GetType("KippixLinks.LinksPanel")
    [KippixLinks.TestHooks]::NoClipboard = $true   # never the real clipboard
    $ctor = $type.GetConstructors($flags)[0]
    $panel = $ctor.Invoke([object[]]@($store.PSObject.BaseObject, $null, $null, $null))
    if ($View -eq "rename" -and $first) { [void]$type.GetMethod("ShowRename", $flags).Invoke($panel, [object[]]@($first.PSObject.BaseObject)) }
    if ($View -eq "confirm") { [void]$type.GetMethod("ShowConfirm", $flags).Invoke($panel, [object[]]@()) }
    $w = $panel.Width; $h = $panel.Height
    $c = $panel.Content
    $c.Measure((New-Object Windows.Size $w, $h)); $c.Arrange((New-Object Windows.Rect 0, 0, $w, $h)); $c.UpdateLayout()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap ([int][Math]::Ceiling($w * $Scale)), ([int][Math]::Ceiling($h * $Scale)), (96 * $Scale), (96 * $Scale), ([Windows.Media.PixelFormats]::Pbgra32)
    $dv = New-Object Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.DrawRectangle($panel.Background, $null, (New-Object Windows.Rect 0, 0, $w, $h))
    $dc.Close()
    $bmp.Render($dv)
    $bmp.Render($c)   # (the element itself, not a VisualBrush of it: a brush would crop the empty padding)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $fs = [IO.File]::Create($Out); try { $enc.Save($fs) } finally { $fs.Close() }
    "$Out ($([int]$w) x $([int]$h) at x$Scale)"
}
finally { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
