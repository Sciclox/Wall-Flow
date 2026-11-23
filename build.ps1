param(
    [switch]$SkipBuild,
    [switch]$SkipInstaller
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $root "publish"
$projectDir = Join-Path $root "WallFlow"

Write-Host "Stopping WallFlow..." -ForegroundColor Cyan
Get-Process WallFlow -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process NavegadorFlow -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Building WallFlow..." -ForegroundColor Cyan

if (-not $SkipBuild) {
    & dotnet build -c Release --nologo -v q -p:Platform=AnyCPU $projectDir
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

Write-Host "Publishing..." -ForegroundColor Cyan
& dotnet publish -c Release --nologo -v q -o $publishDir $projectDir
if ($LASTEXITCODE -ne 0) { exit 1 }

$navegadorProj = Join-Path (Split-Path -Parent $root) "NavegadorFlow\NavegadorFlow.csproj"
if (-not (Test-Path $navegadorProj)) {
    $navegadorProj = "C:\Users\Lenovo\OneDrive\Documentos\Proyectos\NavegadorFlow\NavegadorFlow.csproj"
}
if (Test-Path $navegadorProj) {
    Write-Host "Building optional NavegadorFlow..." -ForegroundColor Cyan
    & dotnet build -c Release --nologo -v q $navegadorProj
    if ($LASTEXITCODE -eq 0) {
        $navegadorBin = Join-Path (Split-Path -Parent $navegadorProj) "bin\Release\net8.0-windows10.0.19041.0"
        if (Test-Path $navegadorBin) {
            Write-Host "Copying NavegadorFlow output to publish folder..." -ForegroundColor Cyan
            Copy-Item -Path "$navegadorBin\*" -Destination $publishDir -Recurse -Force
        }
    }
}

if (-not $SkipInstaller) {
    $iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $iscc = Get-ChildItem "C:\InnoSetup\ISCC.exe" -ErrorAction SilentlyContinue
    }
    if (-not $iscc) {
        $iscc = Get-ChildItem "C:\Program Files*\Inno Setup*\ISCC.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if (-not $iscc) {
        $iscc = Get-ChildItem "$env:LOCALAPPDATA\Programs\Inno Setup*\ISCC.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if ($iscc) {
        Write-Host "Building installer..." -ForegroundColor Cyan
        & $iscc.FullName (Join-Path $root "installer.iss")
    } else {
        Write-Host "Inno Setup not found. Skipping installer." -ForegroundColor Yellow
    }
}

Write-Host "Done!" -ForegroundColor Green
