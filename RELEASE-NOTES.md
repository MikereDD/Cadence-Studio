# Cadence Studio v1.0 — Stable Release

Cadence Studio v1.0 is the first stable release of the modern Windows music player. It preserves the direct, local workflow of Cadence Classic while delivering a separate C#, WPF, XAML, and MVVM application with premium presentation, reliable playback, deep library tools, and a complete release pipeline.

## Validated release experience

- Per-user Windows installer with Start Menu integration and optional desktop shortcut.
- Self-contained and framework-dependent portable packages.
- Upgrade-safe user data stored outside the installation directory.
- Automatic Inno Setup discovery, artifact verification, release manifests, and SHA-256 checksums.
- Successful portable launch, installed launch, library restoration, queue restoration, artwork, lyrics, playback, themes, and detached visualizer validation.

## Included product experience

- Local Library indexing with real folder hierarchy and cached startup.
- Queue and playlist management with persistent session state.
- Compact and expanded Now Playing workspaces.
- Live embedded spectrum and detached Window Visualizer presets.
- Six isolated themes and three typography profiles with adjustable text sizing.
- Real-time equalizer, artwork enrichment, lyrics, artist information, and global search.
- Single-track and album Metadata Workshops with review, backup, selective writes, and verification.
- Song-change notifications, keyboard shortcuts, diagnostics, logs, cache access, and About information.

## Final v1.0 polish

- Improved compact Queue filename readability.
- Simplified finished-product wording throughout the interface.
- Updated the main brand subtitle to **Audio Experience**.
- Updated release-stage badges to **Stable Release**.
- Finalized README and changelog organization.

## Build commands

Portable packages only:

```powershell
.\packaging\New-Release.ps1 -SkipInstaller
```

Complete release set:

```powershell
.\packaging\New-Release.ps1
```

Artifacts are written to:

```text
artifacts\release\1.0
```

User data remains under:

```text
%LOCALAPPDATA%\CadenceStudio
```

Uninstalling Cadence Studio removes the application and shortcuts while intentionally preserving that user-data directory.

## Final release presentation

The public v1.0 repository uses the final Cadence Studio application icon across the app, installer, and README. Release binaries are distributed through GitHub Releases rather than stored in source control.

