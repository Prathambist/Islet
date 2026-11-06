$ErrorActionPreference = 'SilentlyContinue'

Write-Host ""
Write-Host "  =====================================" -ForegroundColor Yellow
Write-Host "        Islet Uninstaller        " -ForegroundColor White
Write-Host "  =====================================" -ForegroundColor Yellow
Write-Host ""

$InstallDir = "$env:LOCALAPPDATA\Islet"
$StartMenuPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Islet.lnk"
$StartupPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\Islet.lnk"

# Islet used to be called MusicIsland; anything left under the old name goes too
$OldInstallDir = "$env:LOCALAPPDATA\MusicIsland"
$OldStartMenuPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\MusicIsland.lnk"
$OldStartupPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\MusicIsland.lnk"

# 1. Stop running processes
Write-Host "Stopping running instances..." -ForegroundColor Yellow
Stop-Process -Name "Islet", "MusicIsland" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

# 2. Remove shortcuts
if (Test-Path $StartMenuPath) {
    Remove-Item -Path $StartMenuPath -Force
    Write-Host "Removed Start Menu shortcut." -ForegroundColor DarkGray
}

if (Test-Path $StartupPath) {
    Remove-Item -Path $StartupPath -Force
    Write-Host "Removed Startup shortcut." -ForegroundColor DarkGray
}

# 3. Remove application files
if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force
    Write-Host "Removed application files from $InstallDir." -ForegroundColor DarkGray
}

# 4. Leftovers from the old name
foreach ($path in $OldInstallDir, $OldStartMenuPath, $OldStartupPath) {
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force
        Write-Host "Removed $path." -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "  Islet has been completely uninstalled." -ForegroundColor Green
Write-Host ""
