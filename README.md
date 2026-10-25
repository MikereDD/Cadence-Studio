<p align="center">
  <img src="src/CadenceStudio.App/Assets/cadence-studio-icon-256.png" width="116" alt="Cadence Studio icon">
</p>

<h1 align="center">Cadence Studio</h1>

<p align="center">
  A polished, local-first Windows music player built with C#, WPF, XAML, MVVM, NAudio, and TagLibSharp.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-1.1-b8872c" alt="Version 1.1">
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-555b66" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/.NET-8-555b66" alt=".NET 8">
  <img src="https://img.shields.io/badge/audio-NAudio%202.3.0-555b66" alt="NAudio 2.3.0">
  <img src="https://img.shields.io/badge/metadata-TagLibSharp%202.3.0-555b66" alt="TagLibSharp 2.3.0">
  <img src="https://img.shields.io/badge/UI-WPF%20%2B%20XAML-2d2d2d" alt="WPF and XAML">
</p>

<p align="center">
  <a href="https://github.com/MikereDD/Cadence-Studio/releases"><strong>GitHub Releases</strong></a>
  ·
  <a href="RELEASE-NOTES.md">Release notes</a>
  ·
  <a href="CHANGELOG.md">Changelog</a>
  ·
  <a href="ROADMAP.md">Roadmap</a>
</p>

---

## Cadence Studio v1.1

Cadence Studio is a modern Windows music player focused on dependable local playback, expressive visuals, strong metadata tools, and a carefully designed desktop experience.

v1.1 builds on the first stable release with persistent background playback in the Windows system tray, global hardware-media-key support, optional per-user Start with Windows integration, audio-state refinements, responsive Now Playing and visualizer improvements, and a hardened signed-update foundation.

**v1.1 is distributed as a manual-install stable release. Automatic update discovery remains unpublished and disabled as a production delivery path.**

Cadence Studio is separate from **Cadence Classic**, the original PowerShell/WinForms application. Both projects retain independent codebases, settings, release histories, and development paths.

## Screenshots

<p align="center">
  <img src="docs/images/cadence-library.png" width="100%" alt="Cadence Studio Library and Now Playing workspace">
</p>

<p align="center"><em>Library, Now Playing, lyrics, queue, visualizer, and transport controls in one focused workspace.</em></p>

<p align="center">
  <img src="docs/images/cadence-visualizer.png" width="100%" alt="Cadence Studio detached Window Visualizer">
</p>

<p align="center"><em>The detached Window Visualizer with responsive live audio, cinematic presets, and fullscreen support.</em></p>

<p align="center">
  <img src="docs/images/cadence-settings.png" width="100%" alt="Cadence Studio Visual Language and typography settings">
</p>

<p align="center"><em>Visual Language, theme isolation, typography profiles, and independent text sizing.</em></p>

## Highlights

### Playback and library

- Local music-library indexing with real folder hierarchy and fast cached startup.
- NAudio playback with seek, volume, shuffle, repeat, queue management, and clean shutdown.
- Persistent queue, playlists, playback position, workspace, window state, and appearance settings.
- Background playback through the Windows system tray with Open, Exit, Previous, Play/Pause, and Next controls.
- Global hardware-media-key support while Cadence Studio is visible, minimized, or hidden in the tray.
- Optional per-user **Start Cadence with Windows** integration.
- M3U and M3U8 playlist import and export.
- Global Library search across artists, albums, tracks, and genres.

### Audio and visualization

- Real-time 10-band equalizer with presets, preamp, custom profiles, and headroom protection.
- Player-volume isolation and filter-state improvements introduced during the v1.1 cycle.
- Full-spectrum FFT visualizers with multiple densities, palettes, response controls, and reduced-motion support.
- Compact and expanded Now Playing workspaces with responsive layout behavior.
- Detached Window Visualizer with **Celestial Resonance**, **Aurora Cascade**, **Glass Horizon**, and **Infinite Windows** presets.
- Corrected mirrored waveform direction and horizon baseline behavior.
- Song-change notifications with artwork, progress, and playback controls.

### Metadata and enrichment

- Embedded and folder artwork support.
- MusicBrainz matching and Cover Art Archive retrieval.
- LRCLIB lyrics and Wikipedia artist information.
- Single-track and full-album Metadata Workshops.
- Reviewable edits, backups, selective writes, artwork embedding, and post-write verification.

### Visual language and accessibility

- Six isolated themes: **Dark Monochrome**, **OLED Black**, **Graphite**, **Midnight Indigo**, **Obsidian Gold**, and **Aurora Pulse**.
- Three typography profiles: **Cadence**, **Precision**, and **Clear**.
- Standard, Large, and Extra Large text sizing independent of Windows display scaling.
- Built-in About, diagnostics, logs, cache locations, and Keyboard Shortcuts reference.

## Install and run

### Download a release

Prebuilt installers and portable packages are published through [**GitHub Releases**](https://github.com/MikereDD/Cadence-Studio/releases). Release binaries are intentionally kept out of the source repository.

v1.1 uses manual installation. The signed updater foundation remains in the codebase, but no stable online discovery manifest is published for automatic updates.

### Windows installer

The installer is the recommended deployment. It installs per-user to:

```text
%LOCALAPPDATA%\Programs\Cadence Studio
```

It creates a Start Menu shortcut, offers an optional desktop shortcut, supports clean upgrades, and preserves Cadence Studio user data during uninstall.

### Portable packages

Two portable variants are produced:

- **Self-contained** — includes the .NET runtime and runs without a separate runtime installation.
- **Framework-dependent** — smaller package that requires the .NET 8 Desktop Runtime.

### Build from source

Requirements:

- Windows 10 or Windows 11
- .NET 8 SDK

```powershell
.\build.ps1 -Run
```

Debug output is written to:

```text
src\CadenceStudio.App\bin\Debug\net8.0-windows\CadenceStudio.exe
```

## Release packaging

Create portable packages only:

```powershell
.\packaging\New-Release.ps1 -SkipInstaller
```

Create the complete release set, including the Inno Setup installer:

```powershell
.\packaging\New-Release.ps1
```

The release script detects Inno Setup 6 from PATH, standard machine-wide locations, and the standard per-user location under `%LOCALAPPDATA%`. An explicit compiler path can also be supplied:

```powershell
.\packaging\New-Release.ps1 `
    -InnoCompilerPath "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
```

Generated packages, manifests, release notes, and SHA-256 checksums are written beneath:

```text
artifacts\release\<version>
```

See [Packaging Documentation](docs/PACKAGING.md) for the complete workflow.

## User data and privacy

Cadence Studio is local-first. It requires no account, subscription, telemetry, or mandatory cloud service.

```text
%LOCALAPPDATA%\CadenceStudio
├── session.json
├── playlists.json
├── cache
│   ├── library-index.json
│   ├── enrichment\<track-hash>.json
│   └── artwork\<musicbrainz-id>-500.jpg
└── logs\cadence-studio.log
```

Online enrichment is optional. Playback, local indexing, playlists, local metadata, and cached content remain available offline.

## Design principles

Cadence Studio aims for **controlled spectacle rather than clutter**: layered depth, tactile controls, expressive visualizers, strong typography, and clear hierarchy without sacrificing reliability, performance, or accessibility.

Stable releases are quality gates, not schedule targets. v1.1 was promoted only after Release-build qualification, manifest/replay security checks, updater transaction regression tests, signed integration tests, and manual packaged-application validation of the preceding development build.

## Documentation

- [GitHub Releases](https://github.com/MikereDD/Cadence-Studio/releases)
- [Testing Notes](TESTING.md)
- [Architecture](ARCHITECTURE.md)
- [Packaging Documentation](docs/PACKAGING.md)
- [Roadmap](ROADMAP.md)
- [Changelog](CHANGELOG.md)
- [Release Notes](RELEASE-NOTES.md)
- [Third-Party Notices](THIRD-PARTY-NOTICES.md)

---

<p align="center">
  <strong>Reliable. Functional. Polished. Eye Candy.</strong>
</p>
