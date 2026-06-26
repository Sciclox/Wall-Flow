# WallFlow Installer
# Usage: powershell -c "iwr -Uri 'https://github.com/Sciclox/Wall-Flow/releases/latest/download/install.ps1' -OutFile install.ps1; .\install.ps1"

param(
    [string]$InstallDir = "$env:LOCALAPPDATA\WallFlow",
    [switch]$AddToStartup
)

$repo = "Sciclox/Wall-Flow"
$apiUrl = "https://api.github.com/repos/$repo/releases/latest"

Write-Host "╔══════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     WallFlow Installer       ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Get latest release
Write-Host "❯ Obteniendo última versión..." -ForegroundColor Green
try {
    $release = Invoke-RestMethod -Uri $apiUrl -ErrorAction Stop
    $tag = $release.tag_name
    $zipAsset = $release.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1

    if (-not $zipAsset) {
        Write-Host "✖ No se encontró el archivo ZIP en la release." -ForegroundColor Red
        exit 1
    }

    $zipUrl = $zipAsset.browser_download_url
    $zipSize = [Math]::Round($zipAsset.size / 1MB, 1)

    Write-Host "❯ Versión: $tag ($zipSize MB)" -ForegroundColor Green
    Write-Host "❯ Descargando..." -ForegroundColor Green

    $zipPath = "$env:TEMP\WallFlow.zip"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing

    Write-Host "❯ Instalando en: $InstallDir" -ForegroundColor Green

    if (Test-Path $InstallDir) {
        Remove-Item -Recurse -Force $InstallDir -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force
    Remove-Item -Path $zipPath -Force

    $exePath = Join-Path $InstallDir "WallFlow.exe"

    if (-not (Test-Path $exePath)) {
        Write-Host "✖ Error: No se encontró WallFlow.exe" -ForegroundColor Red
        exit 1
    }

    # Start Menu shortcut
    $shortcutDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\WallFlow"
    New-Item -ItemType Directory -Path $shortcutDir -Force | Out-Null
    $shortcutPath = Join-Path $shortcutDir "WallFlow.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = "WallFlow - Wallpaper Manager"
    $shortcut.Save()

    # Auto-start shortcut (Startup folder)
    if ($AddToStartup) {
        $startupDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"
        $startupShortcut = Join-Path $startupDir "WallFlow.lnk"
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($startupShortcut)
        $shortcut.TargetPath = $exePath
        $shortcut.IconLocation = "$exePath, 0"
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.Description = "WallFlow - Visor de fondos de pantalla"
        $shortcut.Save()
        Write-Host "❯ Autoarranque configurado" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "╔══════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║   Instalación completada!    ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "   Ejecutar: $exePath" -ForegroundColor White
    Write-Host ""
    Write-Host "   Para desinstalar, elimina la carpeta:" -ForegroundColor Gray
    Write-Host "   $InstallDir" -ForegroundColor Gray
    Write-Host ""

    # Ask to run
    $run = Read-Host "¿Ejecutar WallFlow ahora? (S/n)"
    if ($run -ne "n") {
        Start-Process -FilePath $exePath
    }

} catch {
    Write-Host "✖ Error: $_" -ForegroundColor Red
    exit 1
}
