# Cadence Studio v1.1-dev.7 — Development pre-release

This development build moves the updater into a verified temporary runner beside the installation before handing off an update. The running updater can then replace the installed updater files along with the application.

## Changes

- Copy the installed updater inventory into a transaction-specific sibling directory and verify each copied file with SHA-256 before launch.
- Derive the installation path from the runner's location, bind the runner to the transaction ID, and reject unexpected locations, inventory changes, and differing file contents.
- Preserve updater protocol 3, transaction format 2, independent release verification, one prior-version backup, bounded startup-health confirmation, and rollback behavior.
- Update application and updater version declarations to `1.1-dev.7` and link the runner-location helper into the signed regression fixture.

The existing security baseline includes authenticated schema-3 release manifests, retained sequence replay protection, and pinned `cadence-release-2026-01` ECDSA P-256/SHA-256 release signatures.

## Packages

- Windows x64 self-contained portable ZIP, including the .NET runtime.
- Windows x64 framework-dependent portable ZIP, requiring the .NET 8 Windows Desktop Runtime.
- Per-user Windows x64 installer.
- Canonically named self-contained updater payload: `Cadence-Studio-v1.1-dev.7-win-x64.zip`.

Detached release signatures use the pinned Cadence release identity. These signatures are separate from Windows Authenticode publisher signing.

## Development limitations

- Creating the runner requires write access beside the installation directory. Transaction-specific runner directories are retained after execution; automatic runner cleanup is not included in this increment.
- The staged copy expects the normal published updater inventory. Added empty directories in the installed updater can cause inventory validation to reject the handoff.
- This remains a development pre-release. Local packaging and automated qualification do not establish that the final packaged WPF application has completed an interactive Windows Sandbox update.

## Qualification

The Release regression run on NETZACH on October 18, 2026 passed with 0 build errors and 88 warnings: 18 manifest security/replay checks, 101 signed security/integration checks, and 57 top-level checks. All 30 signed-install scenarios proved the final or restored executable hash. The full log and separate staged-runner qualification evidence accompany the prepared release files.

Publication requires owner approval. No release tag, source commit for these working-tree changes, or published discovery manifest is implied by this local package preparation.

---

## v1.1-dev.5

The update transaction foundation now supports verified downloads, independent updater verification, safe extraction, one prior-version backup, bounded replacement, final hashes, validated restart and rollback of failed replacement.

Production updates fail closed until the release public key is provisioned. See `docs/UPDATER-DEV5.md` for build, tests, key setup and runtime steps. No startup-health handshake is implemented in this increment.

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
