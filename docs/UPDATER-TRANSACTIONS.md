# v1.1-dev.4 — updater transaction foundation

## Baseline and authority

Copy-over overlay for `dev/v1.1`, Cadence commit `028274a7a3114cea2893be3d944a6cbde1d06705` (v1.1-dev.3).
Canonical Typezero-Release-Standards checkout inspected at `90f2720a418eb2c10f9a0bda64da8bbc08196aec`:
`docs/WINDOWS-UPDATER-STANDARD.md`, `docs/UPDATER-PROTOCOL-COMPATIBILITY.md`, and `templates/windows/updater/AppName.Updater/README.md`.
The original checkout was clean and was not modified, committed, or pushed.

## Implemented boundary

The About dialog retains user-initiated discovery and adds **Test updater (no installation)**.
After an eligible discovery result, the transaction uses its candidate version. Otherwise the control creates an explicitly synthetic same-version test. Synthetic is never release eligibility or installation authority.
Cadence stays open. The updater observes whether its exact originating process is running or already closed; it never terminates or restarts a process. PID and UTC process start time must both match the known installed executable. An absent process is accepted only after transaction/install validation.

Build output and packaged layout:

```
CadenceStudio.exe
updater/
  CadenceStudio.Updater.exe
  CadenceStudio.Updater.dll
  CadenceStudio.Updater.deps.json
  CadenceStudio.Updater.runtimeconfig.json
  CadenceStudio.Core.dll
```

The updater derives the trusted installation root from the parent of its own directory. It requires the fixed `CadenceStudio.exe` installed/restart name. Payload names cannot choose either destination.

Transactions live under the OS-resolved local application data directory:
`%LOCALAPPDATA%\CadenceStudio\updates\<32-lowercase-hex-GUID>\transaction.json`.
Creation uses random GUIDs, collision rejection and create-new transaction writes. No environment-supplied staging or install command-line overrides exist. User profile ACL inheritance applies; the updater must run as the same unelevated user as Cadence.

The required local transaction format is version 1. Release manifest schema 2 and discovery protocol 2 are unchanged; this new dry-run format is not advertised as an installation-capable release protocol. Future required installation arguments must follow the canonical protocol migration rules.
Required fields cover app/transaction identity, current/target versions, synthetic flag, installed/restart executable, install/staging roots, reserved payload/signature/extraction/backup paths, PID/start time, creation time and Prepared state.
The JSON is bounded to 32 KiB, with required, unknown and duplicate properties checked. Only Prepared input is accepted. State transitions are appended to a bounded transaction-local `updater.log`: Prepared, Validated, ProcessRunning or ProcessClosed, DryRunCompleted; validated failures may append Failed. Each updater transition first saves transaction.json using a flushed same-directory temporary file and atomic replacement, then appends its log entry. Lifecycle reads may explicitly accept defined saved states; updater handoff still requires Prepared. The updater holds active.lock through failure recording and never mutates a rejected or contended handoff. Failed persistence returns an error; it cannot report success. Saved state is diagnostic, not installation authorization. A crash between JSON replacement and log append can leave JSON ahead of the log; interrupted temporary files are conservatively retained by cleanup. Invalid input is reported on stderr without selecting a log destination from unvalidated data.

Paths must be canonical absolute local-drive paths. Traversal, UNC/device paths, alternate streams, trailing spaces/dots, reserved device names, reparse ancestors, sibling-prefix escapes, unexpected reserved paths and install/staging overlap are rejected. Fixed reserved paths are placeholders; no ZIP or signature content is consumed. The future download increment must replace these placeholders with manifest-bound exact asset metadata before use.

Cleanup examines at most 256 immediate GUID directories on a manual test. Only staging older than seven days, with a transaction.json and exclusively known flat files (transaction.json, updater.log, active.lock), can be removed. Every entry must also be old; an exclusive lease must be acquired. Unexpected content, nested directories and reparse points are retained. Deletion is nonrecursive and paths are rechecked. This deliberately conservative foundation does not claim resistance to a malicious process concurrently rewriting the same user's filesystem; future destructive installation requires handle-based race hardening and independent cryptographic verification.

No payload downloads, actual detached-signature verification, extraction, installed-file replacement, backup copying, health-check relaunch, or rollback are implemented. Prepared/DryRunCompleted must never be interpreted as signature verification or authorization to install.

## Apply and build (PowerShell, repository root)

Exit your running Cadence tray instance before replacing source and rebuilding.
Confirm `git branch --show-current` prints `dev/v1.1` and review any local changes.
Extract the overlay directly into the repository root, allowing its listed files to overwrite matching files.
No files need deleting. Do not copy test binaries into the application installation.

```powershell
.\build.ps1
.\build.ps1 -Configuration Release
dotnet run --project .\tests\CadenceStudio.Updater.Tests\CadenceStudio.Updater.Tests.csproj -c Debug
.\build.ps1 -Run
```

The automated runner builds a separate fixture named CadenceStudio.exe under its own test output so it can test real process identity without opening WPF. It creates transient local-app-data transactions and a temporary junction, tests cleanup and removes its completed flat fixtures through the age policy. A failed run can leave diagnostic staging. Expected after the lifecycle fix: **51 tests passed** and exit code 0.

## Interactive runtime smoke test (pending manual execution)

1. Open Settings → About Cadence Studio → Open About. Confirm v1.1-dev.4 and DEVELOPMENT BUILD.
2. Click Check for updates. Preserve dev.3 behavior: an unpublished endpoint reports the existing clean unavailable message. No transaction is created merely by checking.
3. Click Test updater (no installation). With no eligible result, expect **Synthetic test completed**, a staging/log path, and Cadence still open and usable.
4. Inspect transaction.json and updater.log at that path. Expect Prepared → Validated → ProcessRunning → DryRunCompleted. The payload, extraction and backup paths remain absent.
5. Click again. Expect a different transaction ID and directory.
6. With a genuinely eligible manifest published at the existing approved endpoint, check then test: expect **Eligible update dry run completed**, the candidate version recorded and SyntheticTest false. This path has no bypass allowing arbitrary manifests.
7. Verify playback, media keys, tray behavior, Windows startup preference and the existing About discovery check remain functional. These dev.1–dev.3 modules were not edited.
8. The automated fixture covers a freshly prepared transaction whose process exits before handoff. Completed transactions now reject replay without mutation; do not reuse a completed transaction to test ProcessClosed.

## Publish verification

Use the repository packaging entry point so both processes are published with the same runtime/deployment model:

```powershell
.\packaging\Publish-CadenceStudio.ps1 -Runtime win-x64 -Deployment FrameworkDependent
# Optional additional release matrix (not executed for this overlay):
.\packaging\Publish-CadenceStudio.ps1 -Runtime win-x64 -Deployment SelfContained
.\packaging\Publish-CadenceStudio.ps1 -Runtime win-arm64 -Deployment SelfContained
```

Confirm the published updater subfolder contains its executable and runtime configuration. Raw app-only `dotnet publish` is not the packaging contract. No release was published remotely.

## Validation performed for this overlay

Windows/.NET SDK 8.0.424: Debug solution build PASS; Release solution build PASS (93 analyzer warnings, zero errors); x64 framework-dependent packaging PASS; 37 automated checks PASS.
Tests cover live and already-exited process handoffs, identity conflicts, missing/extra/install-mode arguments, transaction ID/app/format/state/version errors, restart/backup/payload escapes, traversal/device/stream/trailing-dot paths, duplicate/unknown/missing/oversized JSON, multipart dev version ordering, logging, absent payload/backup, unknown-content and active-lease retention, junction rejection/retention and expired cleanup.
Interactive WPF smoke, eligible remote-manifest UI flow, ARM64 execution and self-contained publishing were not executed. No signature, replacement or rollback claims are made.
