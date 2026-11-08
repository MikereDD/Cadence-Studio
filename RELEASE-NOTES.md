# Cadence Studio v1.3.1 — Stable Maintenance Release

Cadence Studio v1.3.1 is a focused interface-copy and usability refinement of v1.3. It retains the monitor-aware fullscreen visualizer, playback engine, library, and settings compatibility.

## Highlights

- Removed Cadence Classic messaging from the app and current-facing documentation.
- Simplified playback status labels and reduced duplicated library count text.
- Cleaned up lyrics empty states and repeated source attribution; credits remain visible when lyrics exist.
- Made the About dialog's Updates section more compact, with updater test controls under developer diagnostics.
- Show the update-install action only when a valid, eligible update is identified.

## Distribution

Manual-install stable release: per-user Windows installer and both portable ZIP variants, each accompanied by a SHA-256 checksum. The signed-updater foundation remains in place, but no stable online automatic-update discovery manifest is being published.

## Verification

Release build, audio/security/updater tests, package checksum verification, and a manual packaged application/installer smoke test are required before publication.

---
# Cadence Studio v1.3 — Stable Release

Cadence Studio v1.3 brings flexible multi-monitor fullscreen rendering to the detached Window Visualizer, preserving the interface and transport refinements from the v1.2 cycle.

## Highlights

- **Current Display** (default): fullscreen on the monitor hosting the visualizer.
- **All Displays**: one independently sized visualizer per connected screen, using the same audio-driven view model.
- **Selected Display**: target an explicitly chosen Windows monitor.
- Better framing on mixed resolutions and aspect ratios, including 16:9 and 16:10 layouts.
- Fullscreen monitor navigation and window restoration controls.

## Distribution

Manual-install stable release: self-contained and framework-dependent portable ZIPs plus per-user Windows installer, each with a SHA-256 sidecar. The signed-updater foundation remains present; the stable online automatic-update discovery manifest is intentionally not published.

## Verification

Build, audio, manifest security, updater regression, signed integration, and release artifact checks are required before publication. Install/launch and multi-monitor fullscreen behavior are manually smoke-tested before publishing.

---
# Cadence Studio v1.2 — Stable Release

This release focuses on interface consistency, button behavior, layout corrections and improved song-change notifications.

## Highlights

- Unified themed transport, Library, Queue, playlist and dialog controls.
- Improved responsive Music Library and compact Queue layouts.
- Cadence Studio icon fallback when album artwork is unavailable.
- Song-change notification controls matching the main player.

## Release qualification

Release build verified with zero compilation errors. Audio regression: 7 passed, 0 failures. Manifest security: 18 passed. Updater transactions: 56 passed. Signed integration: 101 passed. Top-level checks: 57 passed.
v1.2 is distributed through manual installer and portable packages. No stable automatic-update discovery manifest is published.

---

# Cadence Studio v1.1 — Stable Release

Cadence Studio v1.1 is the second stable release of the Windows music player. It keeps the local-first playback and library workflow of v1.0 while adding a stronger Windows background-player experience, refined audio and visual behavior, and a hardened signed-update foundation.

## Release model

v1.1 is a **manual-install stable release**.

The updater foundation remains present and fully regression-tested, but the stable online discovery manifest is intentionally **not published**. Automatic updating is therefore not an approved production delivery path for this release. Users should install or upgrade v1.1 through the published installer or portable packages.

## Highlights

### Windows background player

- Persistent system-tray presence using the Cadence Studio application icon.
- Closing the main window can send Cadence Studio to the tray while playback continues.
- Optional minimize-to-tray behavior.
- Tray controls for Open, Exit, Previous, Play/Pause, and Next.
- Current-track tray tooltip text.
- Improved single-instance behavior so relaunching Cadence Studio restores the existing window.

### Media keys and startup

- Global Previous, Play/Pause, Stop, and Next hardware-media-key handling while Cadence Studio is visible, minimized, or hidden.
- Nonfatal registration when another application already owns an individual media key.
- Optional **Start Cadence with Windows** preference.
- Per-user Windows startup registration with no administrator requirement.

### Audio and visual refinements

- Improved equalizer headroom and filter-state behavior.
- Player-volume isolation from unrelated audio state.
- Responsive Now Playing and visualizer layout improvements.
- Corrected mirrored waveform direction and horizon baseline behavior.
- Refreshed public screenshots representing the current v1.1 interface.

### Update-security foundation

The v1.1 development cycle established and qualified:

- Authenticated schema-3 release manifests.
- Positive manifest sequencing with retained replay protection.
- Pinned Cadence release signing identity and detached ECDSA signatures.
- Independent application/updater release verification.
- Strict package identity, path, archive, hash, and signature validation.
- One known-good backup.
- Bounded startup-health confirmation.
- Rollback on failed replacement or failed health confirmation.
- Transaction-specific staged updater runners with copied-file SHA-256 verification.

These mechanisms remain dormant for public delivery until a stable online discovery manifest is intentionally published and the live automatic-update path is separately approved.

## Packages

The stable v1.1 release pipeline produces:

- `CadenceStudio-1.1-win-x64-self-contained-portable.zip`
- `CadenceStudio-1.1-win-x64-framework-dependent-portable.zip`
- `CadenceStudio-1.1-win-x64-Setup.exe`

SHA-256 checksum sidecars are produced and verified by the packaging workflow.

The self-contained portable package includes the .NET runtime. The framework-dependent package requires the .NET 8 Windows Desktop Runtime. The installer is per-user and preserves `%LOCALAPPDATA%\CadenceStudio` across upgrades and uninstall.

## Qualification

The stable-promotion Release qualification on NETZACH on October 25, 2026 completed with:

- 100 analyzer warnings and **0 build errors**.
- **18/18** manifest security and replay checks.
- **56/56** updater transaction regression checks.
- **101/101** signed security/integration checks.
- **57/57** top-level updater checks.

The negative updater cases intentionally produced rejection messages for tampered hashes, signatures, identities, paths, packages, version transitions, reparse points, and protocol mismatches. Those expected rejections are part of the passing security suite.

Before stable promotion, the packaged v1.1-dev.7 self-contained portable build and per-user installer were also manually exercised on NETZACH. The packaged application launched correctly, restored the existing library and queue, rendered artwork and visualizers, played audio, preserved user state, and the installer ran from the expected `%LOCALAPPDATA%\Programs\Cadence Studio` path.

Final stable `1.1` packages are produced only after this promotion metadata and documentation are in place, then receive the final package smoke test before publication.

## Automatic updates

No `updates/stable/release-manifest.json` is published for v1.1.

The About-dialog update check therefore remains a safe no-manifest path rather than initiating an automatic update. This is intentional for the v1.1 stable release.

## User data

Cadence Studio user data remains under:

```text
%LOCALAPPDATA%\CadenceStudio
```

Uninstalling or upgrading the application intentionally preserves this directory.

---

# Cadence Studio v1.0 — Stable Release

Cadence Studio v1.0 was the first stable standalone release of the modern Windows music player. It established the local-first library, queue and playlist workflow, metadata tools, visualizer suite, theme system, equalizer, notifications, packaging pipeline, and per-user installer that v1.1 builds upon.
