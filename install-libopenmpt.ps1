# install-libopenmpt.ps1
# Downloads libopenmpt.dll AND its companion DLLs from the VS2022 dev package
# and installs them next to the amChipper executable.
#
# NOTE: libopenmpt.dll dynamically links against companion DLLs:
#   openmpt-mpg123.dll, openmpt-ogg.dll, openmpt-vorbis.dll, openmpt-zlib.dll
# All of them must be present or the DLL will silently fail to load.
#
# Run from the solution root: Right-click → Run with PowerShell
# (or: powershell -ExecutionPolicy Bypass -File install-libopenmpt.ps1)

$ErrorActionPreference = "Stop"

$devZipUrl = "https://lib.openmpt.org/files/libopenmpt/dev/libopenmpt-0.8.6+release.dev.windows.vs2022.zip"
$targetDir  = Join-Path $PSScriptRoot "src\amChipper.App\bin\x64\Release\net10.0-windows"
$tmpZip     = Join-Path $env:TEMP "libopenmpt-dev.zip"

Write-Host ""
Write-Host "=== amChipper — libopenmpt installer ===" -ForegroundColor Cyan
Write-Host ""

# ── 1. Ensure target directory exists ─────────────────────────────────────────
if (-not (Test-Path $targetDir)) {
    Write-Host "Creating output directory..."
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

# ── 2. Download the dev package ───────────────────────────────────────────────
Write-Host "Downloading libopenmpt dev package from lib.openmpt.org..." -ForegroundColor Cyan
Write-Host "  URL: $devZipUrl"
Invoke-WebRequest -Uri $devZipUrl -OutFile $tmpZip -UserAgent "amChipper/1.0"
$zipSize = (Get-Item $tmpZip).Length
Write-Host "  Downloaded: $([math]::Round($zipSize/1MB, 1)) MB"

# ── 3. Extract all DLLs from bin/amd64/ ───────────────────────────────────────
Write-Host "Extracting DLLs from bin/amd64/..." -ForegroundColor Cyan

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($tmpZip)

$amd64Dlls = $zip.Entries | Where-Object { $_.FullName -like "bin/amd64/*.dll" }

if ($null -eq $amd64Dlls -or @($amd64Dlls).Count -eq 0) {
    $zip.Dispose()
    Write-Error "No DLLs found under bin/amd64/ in the zip."
    exit 1
}

foreach ($entry in $amd64Dlls) {
    $outPath = Join-Path $targetDir $entry.Name
    # Remove existing copy first
    if (Test-Path $outPath) { Remove-Item $outPath -Force }
    $s = $entry.Open()
    $f = [System.IO.File]::Create($outPath)
    $s.CopyTo($f)
    $f.Dispose()
    $s.Dispose()
    Write-Host "  Installed: $($entry.Name)  ($([math]::Round($entry.Length/1KB)) KB)"
}

$zip.Dispose()
Remove-Item $tmpZip -Force

# ── 4. Verify ─────────────────────────────────────────────────────────────────
$mainDll = Join-Path $targetDir "libopenmpt.dll"
if (-not (Test-Path $mainDll)) {
    Write-Error "libopenmpt.dll not found after extraction!"
    exit 1
}

Write-Host ""
Write-Host "SUCCESS!" -ForegroundColor Green
Write-Host "  libopenmpt.dll: $([math]::Round((Get-Item $mainDll).Length/1MB, 2)) MB"
Write-Host "  All companion DLLs installed."
Write-Host ""
Write-Host "Rebuild the solution in Visual Studio, then run amChipper." -ForegroundColor Green
Write-Host ""
Read-Host "Press Enter to exit"
