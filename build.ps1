# build.ps1 — Compile ScreenOff.cs to a standalone Windows system tray application.

$ErrorActionPreference = 'Stop'

$scriptDir = $PSScriptRoot
$srcFile   = Join-Path $scriptDir "ScreenOff.cs"
$outExe    = Join-Path $scriptDir "screen-off.exe"

# Locate csc.exe (the C# compiler bundled with .NET Framework)
$csc = "$env:windir\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
}

if (-not (Test-Path $csc)) {
    Write-Host "ERROR: C# compiler (csc.exe) not found." -ForegroundColor Red
    Write-Host "csc.exe is pre-installed on Windows 10/11." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $srcFile)) {
    Write-Host "ERROR: Source file not found at $srcFile" -ForegroundColor Red
    exit 1
}

Write-Host "=== Building Screen Off Standalone Utility ===" -ForegroundColor Cyan
Write-Host "Source:   $srcFile"
Write-Host "Output:   $outExe"
Write-Host "Compiler: $csc"
Write-Host ""

Write-Host "Compiling..." -ForegroundColor Cyan
& $csc /nologo /target:winexe `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /reference:System.dll `
    /out:"$outExe" `
    "$srcFile" 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "Compilation failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

if (-not (Test-Path $outExe)) {
    Write-Host "ERROR: Compilation finished successfully but output executable was not found." -ForegroundColor Red
    exit 1
}

$sizeBytes = (Get-Item $outExe).Length
$sizeMB    = "{0:N2}" -f ($sizeBytes / 1MB)
$hash      = (Get-FileHash -Path $outExe -Algorithm SHA256).Hash

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Green
Write-Host "Output: $outExe"
Write-Host "Size:   $sizeMB MB ($sizeBytes bytes)"
Write-Host "SHA256: $hash"
Write-Host ""
Write-Host "Run screen-off.exe to start the utility in the system tray." -ForegroundColor Yellow
