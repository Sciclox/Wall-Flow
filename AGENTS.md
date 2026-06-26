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
