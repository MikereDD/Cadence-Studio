# Cadence Studio v1.1-dev.6 startup-health overlay

Base: GitHub `MikereDD/Cadence-Studio`, branch `dev/v1.1`, commit
`0d2866ca0ebe234dd9ecadb7aac3c54062013fbb`.

Standards target: Typezer∅ Release Standards branch
`feature/release-security-vnext`, specifically the Windows bounded startup-health
handshake and updater-protocol compatibility requirements. The branch is a candidate
standard; Cadence is using it as the engineering target without claiming it has become
canonical.

## Scope

This increment adds the dev.6A transaction-scoped startup-health contract:

- updater protocol 3;
- local transaction format 2;
- a randomized 256-bit per-transaction health token;
- a reserved `%LOCALAPPDATA%\CadenceStudio\updates\<transaction-id>\health.json` marker;
- a child-process health handoff containing transaction ID, target version and marker path;
- the health token delivered through the child environment rather than the command line;
- HMAC-SHA-256 proof over transaction ID, target version, PID and process start time;
- exact launched-process identity validation;
- a 30-second bounded startup-health wait;
- rollback to the one verified prior installation on startup exit, invalid proof or timeout;
- no automatic retry after rollback;
- terminal update success only after `HealthConfirmed`.

The existing Cadence release identity `cadence-release-2026-01` remains ECDSA P-256 / SHA-256.
The vNext candidate explicitly allows an existing valid P-256 / SHA-256 identity to remain in
use rather than rotating it merely because P-384 / SHA-384 is preferred for new identities.

Signed-manifest authentication, manifest-sequence replay protection, multiple release-key
identities/recovery authority and the remaining vNext release-security work are intentionally
left for the next dev.6 slice.

## Apply

Apply the runtime overlay first, then the tests/docs overlay at the repository root. Both are
repo-relative replacement overlays.

## Validate on Windows

Exit Cadence Studio completely, including the tray process, then run:

```powershell
.\scripts\Test-Dev6.ps1
```

The executable harness, not `dotnet test`, is authoritative for this increment. It preserves
the dev.4/dev.5 tamper, path, signing, rollback and protocol checks and adds explicit startup
health cases. In particular it must prove:

- a valid signed update reaches `HealthConfirmed`;
- a previous supported updater protocol path still succeeds when the release permits it;
- an updater below the minimum protocol rejects before replacement;
- early startup failure rolls back to the previous known-good installation;
- an invalid startup-health proof rolls back;
- rollback transactions cannot replay or auto-retry;
- the final/restored `CadenceStudio.exe` hash is the expected trusted hash.

Do not commit or push until the Windows harness is green. Analyzer warnings that predate this
increment are not part of the dev.6A feature scope unless they become build errors.
