# Cadence Studio v1.1-dev.3 — Development Build

## v1.1-dev.4 — Separate updater and staging transactions

- Added a separate CadenceStudio.Updater executable with a dry-run-only command contract.
- Added randomized local-app-data staging, strict transaction/path validation, process identity checks and already-exited handling.
- Added transaction state logging, exclusive active leases and conservative abandoned-staging cleanup.
- Added the About-dialog test handoff, including an explicitly synthetic offline test when no update is eligible.
- Included updater build/publish wiring and 37 automated regression checks.
- Preserved discovery behavior. No payload download, signature verification, installed-file replacement or rollback is enabled.
- See docs/UPDATER-TRANSACTIONS.md for exact build/runtime tests and validation limits.


The third v1.1 development increment establishes the read-only updater discovery foundation. Cadence Studio can now perform a user-initiated check against a pinned, approved HTTPS manifest endpoint and strictly validate release identity, channel, architecture, manifest schema, numeric version ordering, source metadata, payload metadata, detached-signature metadata, rollback policy, and updater-protocol compatibility before declaring an update eligible.

## Added in dev.3

- Canonical Typezer∅ release-manifest revision 2 data model.
- Strict rejection of unknown JSON properties.
- Cadence-specific manifest validation for:
  - `appId`, product name, Windows platform, architecture, and channel;
  - Stable/development version grammar and numeric multi-part comparison;
  - source repository, exact `v<version>` tag, and full commit identifier;
  - canonical updater payload filename;
  - exact asset size/hash metadata;
  - detached-signature metadata and pinned-key fingerprint metadata shape;
  - Windows rollback policy retaining one known-good version;
  - updater protocol compatibility and direct-update minimum version.
- Approved development manifest endpoint pinned to the Cadence Studio GitHub repository over HTTPS.
- 256 KiB manifest size ceiling and bounded network timeout.
- A manual **Check for updates** control in About.
- Clear dev.3 boundary: discovery and compatibility checks only.

## Intentionally not implemented yet

v1.1-dev.3 does **not** download release payloads, create staging transactions, verify actual downloaded hashes/signatures, launch a separate updater, replace installed files, perform startup-health confirmation, or roll back. Those operations belong to the later updater increments.

Until a development manifest is published at the approved endpoint, the expected runtime result is a clean **No development update manifest is published...** message rather than a failure or crash.

The tray lifecycle, media keys, and optional Start with Windows behavior from dev.1 and dev.2 are retained.

This is a development build and is not the stable v1.0 release.

---

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
