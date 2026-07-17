# Packaging Cadence Studio

Cadence Studio uses `dotnet publish` for application deployment and Inno Setup 6 for the Windows installer.

## User-data boundary

Installed program files live under `%LOCALAPPDATA%\Programs\Cadence Studio`.
Persistent data lives under `%LOCALAPPDATA%\CadenceStudio` and is never included in the installer directory. This prevents an update or uninstall from removing the library index, playlists, settings, cache, logs, diagnostics, tag backups, or session state.

## Quick release

```powershell
.\packaging\New-Release.ps1
```

Outputs:

- self-contained portable ZIP
- framework-dependent portable ZIP
- per-user installer
- SHA-256 checksum files
- release manifest inside each published application folder

## Portable-only release

```powershell
.\packaging\New-Release.ps1 -SkipInstaller
```

## Other architectures

```powershell
.\packaging\New-Release.ps1 -Runtime win-arm64
```

## Upgrade test

1. Install an older build.
2. Add a music root, create a playlist, select a theme, and close Cadence Studio.
3. Install the new setup over the existing installation.
4. Confirm the library root, playlist, theme, visualizer settings, queue/session, and caches remain.
5. Uninstall Cadence Studio.
6. Confirm `%LOCALAPPDATA%\CadenceStudio` remains.
7. Reinstall and confirm the preserved data is restored automatically.
