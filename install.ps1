# WallFlow Installer
# Usage: powershell -c "iwr -Uri 'https://github.com/Sciclox/Wall-Flow/releases/latest/download/install.ps1' -OutFile install.ps1; .\install.ps1"

param(
    [string]$InstallDir = "$env:LOCALAPPDATA\WallFlow",
    [switch]$AddToStartup
)

$repo = "Sciclox/Wall-Flow"
$apiUrl = "https://api.github.com/repos/$repo/releases/latest"

Write-Host "=== WallFlow Installer ===" -ForegroundColor Cyan
Write-Host ""

# Check .NET Runtime
$dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
$hasRuntime = $false
if ($dotnet) {
    $info = dotnet --info 2>$null
    if ($info -match "Microsoft\.NETCore\.App.*8\.\d+\.\d+") {
        $hasRuntime = $true
    }
}

if (-not $hasRuntime) {
    Write-Host "[!] .NET 8 Runtime no detectado." -ForegroundColor Yellow
    Write-Host "    Descárgalo desde: https://dotnet.microsoft.com/download/dotnet/8.0"
    Write-Host "    O instálalo con: winget install Microsoft.DotNet.DesktopRuntime.8"
    $opt = Read-Host "    ¿Continuar de todas formas? (s/N)"
    if ($opt -ne "s") { exit 1 }
}

# Get latest release
Write-Host "[*] Obteniendo última versión..." -ForegroundColor Green
try {
    $release = Invoke-RestMethod -Uri $apiUrl -ErrorAction Stop
    $tag = $release.tag_name
    $zipUrl = ($release.assets | Where-Object { $_.name -like "*.zip" }).browser_download_url

    if (-not $zipUrl) {
        Write-Host "[!] No se encontró el archivo ZIP en la release." -ForegroundColor Red
        exit 1
    }

    Write-Host "[*] Versión: $tag" -ForegroundColor Green
    Write-Host "[*] Descargando: $zipUrl" -ForegroundColor Green

    $zipPath = "$env:TEMP\WallFlow.zip"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath

    # Extract
    Write-Host "[*] Extrayendo a: $InstallDir" -ForegroundColor Green
    if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force
    Remove-Item -Path $zipPath -Force

    # Create shortcut
    $exePath = Join-Path $InstallDir "WallFlow.exe"
    if (Test-Path $exePath) {
        $shortcutDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\WallFlow"
        New-Item -ItemType Directory -Path $shortcutDir -Force | Out-Null
        $shortcutPath = Join-Path $shortcutDir "WallFlow.lnk"
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $exePath
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.Description = "WallFlow - Wallpaper Manager"
        $shortcut.Save()
        Write-Host "[+] Acceso directo creado en el menú Inicio" -ForegroundColor Cyan
    }

    Write-Host ""
    Write-Host "=== Instalación completada ===" -ForegroundColor Cyan
    Write-Host "Ejecuta: $exePath" -ForegroundColor White
    Write-Host ""
    Write-Host "Para desinstalar, elimina la carpeta: $InstallDir" -ForegroundColor Gray

} catch {
    Write-Host "[!] Error: $_" -ForegroundColor Red
    exit 1
}
