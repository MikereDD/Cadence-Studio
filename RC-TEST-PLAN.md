# Cadence Studio v1.0-rc.2 Test Plan

## Deployment

- Build both portable packages with `New-Release.ps1 -SkipInstaller`.
- Build the complete installer release with `New-Release.ps1`.
- Confirm all generated SHA-256 files validate.
- Launch the self-contained portable build on a machine without relying on the .NET desktop runtime.
- Launch the framework-dependent build on a machine with .NET 8 Desktop Runtime.
- Perform a fresh installer installation.
- Install over the previous development build and verify a clean upgrade.
- Confirm Start Menu and optional desktop shortcuts.
- Uninstall and verify application binaries and shortcuts are removed while `%LOCALAPPDATA%\CadenceStudio` remains.

## Persistence and recovery

- Confirm the indexed library, playlists, queue, settings, theme, typography, cache, and session survive upgrades.
- Start with the music drive disconnected and reconnect it later.
- Test a moved or deleted track.
- Test malformed or empty session/cache files and confirm graceful recovery.
- Confirm offline startup and MusicBrainz unavailability do not block the application.

## Playback and interface

- Run a long playback session and exercise pause, seek, previous/next, repeat, and shuffle.
- Switch repeatedly between compact and expanded modes.
- Open, close, pin, restore, resize, maximize, and fullscreen the Window Visualizer.
- Test all visualizer presets and all six themes.
- Verify queue and playlist operations after restart.
- Test global search across artists, albums, tracks, and genres.
- Open About, Keyboard Shortcuts, Settings, and both metadata workshops.
- Verify clean shutdown with playback active and with detached windows open.

## Release gate

Stable v1.0 is approved only when functionality, reliability, performance, usability, packaging, upgrade safety, and visual polish all pass this plan without release-blocking defects.
