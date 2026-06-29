# Build Instructions

## Build everything (code + installer)
```powershell
.\build.ps1
```

## Or step by step

### 1. Build the project
```powershell
dotnet build -c Release
```
From: `WallFlow\` directory

### 2. Publish (use ABSOLUTE path, not relative)
```powershell
dotnet publish -c Release -o "C:\full\path\to\Wall-Flow\publish"
```

### 3. Build installer (requires Inno Setup)
```powershell
ISCC.exe installer.iss
```

## Troubleshooting
- If build fails because `WallFlow.exe` is locked: kill the process first
  ```powershell
  Get-Process WallFlow -ErrorAction SilentlyContinue | Stop-Process -Force
  ```
- The `publish\` directory is in `.gitignore` — only the final `installer\WallFlow-Setup.exe` is tracked

# UHDPaper Scraper

## Setup
```powershell
# Install dependencies
pip install -r scripts/requirements.txt

# Dry-run (list without downloading)
python scripts/uhdpaper_scraper.py --dry-run -p 1

# Download 3 pages of 4K wallpapers
python scripts/uhdpaper_scraper.py -r 4k -p 3 -o ./wallpapers

# All resolutions: 4k, 2k, hd, phone-4k, phone-hd
```

> **Note:** If `python` isn't found, add `$env:LOCALAPPDATA\Programs\Python\Python313\` and `$env:LOCALAPPDATA\Programs\Python\Python313\Scripts\` to your User PATH before `%LOCALAPPDATA%\Microsoft\WindowsApps`.
