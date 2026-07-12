# install.ps1 — installer for the standalone screen-off utility.
# Run from PowerShell: powershell -ExecutionPolicy Bypass -File install.ps1

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$binDir      = Join-Path $env:USERPROFILE ".local\bin"
$exeName     = "screen-off.exe"
$srcName     = "ScreenOff.cs"

Write-Host "=== Screen Off Standalone Utility Installer ===" -ForegroundColor Cyan
Write-Host "User Profile: $env:USERPROFILE"
Write-Host ""

# 1. Create .local\bin if it doesn't exist
if (-not (Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
    Write-Host "Created $binDir" -ForegroundColor Green
}

# 2. Locate and compile using csc.exe
$csc = "$env:windir\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
}

if (-not (Test-Path $csc)) {
    Write-Host "ERROR: C# compiler (csc.exe) not found." -ForegroundColor Red
    exit 1
}

$srcPath = Join-Path $projectRoot $srcName
$outPath = Join-Path $projectRoot $exeName

Write-Host "Compiling ScreenOff.cs..." -ForegroundColor Cyan
& $csc /nologo /target:winexe `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /reference:System.dll `
    /out:"$outPath" `
    "$srcPath" 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "Compilation failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

# 3. Copy screen-off.exe to %USERPROFILE%\.local\bin
$dstPath = Join-Path $binDir $exeName

# Kill existing running instances of screen-off to allow overwrite
$existingProcs = Get-Process -Name "screen-off" -ErrorAction SilentlyContinue
if ($existingProcs) {
    Write-Host "Stopping running instances of screen-off..." -ForegroundColor Yellow
    Stop-Process -Name "screen-off" -Force
    Start-Sleep -Seconds 1
}

Copy-Item -Path $outPath -Destination $dstPath -Force
Write-Host "Installed: $dstPath" -ForegroundColor Green

# 4. Add %USERPROFILE%\.local\bin to user PATH if not present
$userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($userPath -notlike "*$binDir*") {
    [Environment]::SetEnvironmentVariable("PATH", "$userPath;$binDir", "User")
    $env:PATH = "$env:PATH;$binDir"
    Write-Host "Added $binDir to user PATH" -ForegroundColor Green
} else {
    Write-Host "$binDir already on user PATH" -ForegroundColor DarkGray
}

# 5. Start the utility
Write-Host "Starting Screen Off Utility..." -ForegroundColor Cyan
Start-Process -FilePath $dstPath

Write-Host ""
Write-Host "=== Installation Complete ===" -ForegroundColor Green
Write-Host "The utility is now running in your system tray." -ForegroundColor Green
Write-Host "Double-click the tray icon to open the dashboard." -ForegroundColor Green
Write-Host "Hotkey default: Alt+D. Customize it in the dashboard settings." -ForegroundColor Yellow
Write-Host ""
