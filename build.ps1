param(
    [switch]$SkipBuild,
    [switch]$SkipInstaller
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $root "publish"
$projectDir = Join-Path $root "WallFlow"

Write-Host "Building WallFlow..." -ForegroundColor Cyan

if (-not $SkipBuild) {
    & dotnet build -c Release --nologo -v q -p:Platform=AnyCPU $projectDir
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

Write-Host "Publishing..." -ForegroundColor Cyan
& dotnet publish -c Release --nologo -v q -o $publishDir $projectDir
if ($LASTEXITCODE -ne 0) { exit 1 }

if (-not $SkipInstaller) {
    $iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $iscc = Get-ChildItem "C:\InnoSetup\ISCC.exe" -ErrorAction SilentlyContinue
    }
    if (-not $iscc) {
        $iscc = Get-ChildItem "C:\Program Files*\Inno Setup*\ISCC.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if ($iscc) {
        Write-Host "Building installer..." -ForegroundColor Cyan
        & $iscc.FullName (Join-Path $root "installer.iss")
    } else {
        Write-Host "Inno Setup not found. Skipping installer." -ForegroundColor Yellow
    }
}

Write-Host "Done!" -ForegroundColor Green
