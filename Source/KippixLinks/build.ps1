# Builds KippixLinks.dll with the C# compiler that ships with Windows (.NET Framework 4, C# 5).
#   -Install  copies it into Playnite's Extensions folder (Playnite must be closed)
#   -Package  makes KippixLinks-<version>.pext next to this script
param([switch]$Install, [switch]$Package)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319"   # Playnite is a 32-bit process; the dll is anycpu
$wpf = Join-Path $fw "WPF"
$playnite = "$env:LOCALAPPDATA\Playnite"
$sdk = Join-Path $playnite "Playnite.SDK.dll"
if (-not (Test-Path $sdk)) { Write-Host "Playnite.SDK.dll not found in $playnite"; exit 1 }
$out = Join-Path $root "build"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null
$refs = @($sdk, "$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Web.Extensions.dll", "$fw\System.Security.dll", "$fw\System.Xaml.dll",
    "$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll")
$a = @("/nologo", "/target:library", "/optimize+", "/platform:anycpu", "/warn:4", "/codepage:65001", "/out:$out\KippixLinks.dll")
$a += $refs | ForEach-Object { "/r:$_" }
$a += Get-ChildItem (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName }
& "$fw\csc.exe" @a
if ($LASTEXITCODE -ne 0) { Write-Host "COMPILE FAILED"; exit 1 }
Copy-Item (Join-Path $root "extension.yaml") $out
Write-Host "Compiled OK: $out\KippixLinks.dll"

$version = ((Get-Content (Join-Path $root "extension.yaml")) | Where-Object { $_ -match '^Version:\s*(.+)$' } | ForEach-Object { $Matches[1].Trim() } | Select-Object -First 1)

if ($Package) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $pext = Join-Path $root ("KippixLinks-" + $version + ".pext")
    if (Test-Path $pext) { Remove-Item $pext -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($out, $pext)
    Write-Host "Packaged: $pext"
}

if ($Install) {
    if (Get-Process -Name "Playnite.DesktopApp", "Playnite.FullscreenApp" -ErrorAction SilentlyContinue) { Write-Host "Playnite is running: not installed"; exit 2 }
    $dest = "$env:APPDATA\Playnite\Extensions\KippixLinks_08ee0e70-aeb1-4ad0-a0d7-14f23832c57d"
    New-Item -ItemType Directory $dest -Force | Out-Null
    Copy-Item "$out\KippixLinks.dll", "$out\extension.yaml" $dest -Force
    Write-Host "Installed to $dest"
}
