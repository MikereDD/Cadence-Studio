# Cadence Studio v1.1-dev.6B signed-manifest and replay-security overlay

Base: GitHub `MikereDD/Cadence-Studio`, branch `dev/v1.1`, commit
`8739760` (`feat(cadence-studio): add startup health rollback for v1.1-dev.6`).

Standards target: Typezer∅ Release Standards branch
`feature/release-security-vnext`. That branch still identifies itself as
**EXPERIMENTAL — NOT CANONICAL**. Cadence is using it as an engineering and
validation target rather than claiming vNext has replaced the canonical standard.

## Scope

This dev.6B increment adds the next release-host-compromise defenses without
changing the dev.6 updater command/replacement protocol:

- release-manifest schema revision 3;
- required positive `manifestSequence`;
- detached `release-manifest.json.sig` fetched only from the approved Cadence
  raw-GitHub origin;
- signature verification over the exact published `release-manifest.json` bytes
  **before** any remote manifest field is deserialized or acted upon;
- explicit RFC 3279 DER ECDSA verification;
- the existing `cadence-release-2026-01` P-256 / SHA-256 identity remains active;
- local trust is now represented as a multi-key/role/lifecycle collection rather
  than a single remote-selectable key;
- lifecycle groundwork for `pending`, `active`, `retiring`, `retired`, and
  `revoked` release identities plus a separate `recovery` role;
- only `active` or `retiring` **release** identities may authorize routine releases;
- the manifest cannot establish or replace local trust by naming a key;
- trusted replay state retained outside the installation directory;
- an older authenticated `manifestSequence` is rejected after a newer sequence
  has been observed;
- reusing the same sequence number for different exact manifest bytes is rejected;
- a repeated check of the same authenticated sequence and exact bytes remains valid;
- corrupt replay state fails closed rather than silently resetting trust history.

The manifest state records only app/channel, highest sequence, exact-manifest
SHA-256, published timestamp, and local observation time. It contains no secret.

## Cryptographic compatibility

Cadence does **not** rotate `cadence-release-2026-01` merely to adopt vNext.
The current identity remains:

```text
ECDSA P-256
SHA-256
RFC 3279 DER detached signatures
SHA-256(DER SubjectPublicKeyInfo) fingerprint
```

The trust model also understands the vNext candidate profile identifier
`typezero-ecdsa-p384-sha384-v1` for a future deliberately provisioned identity.
No P-384 production identity is introduced by this overlay.

The public-key provisioning helper was updated so future reviewed trust-anchor
changes write the multi-key trust structure and can validate either the existing
P-256 profile or the candidate P-384 profile. It still accepts **public material
only** and never handles the production private key or passphrase.

## Replay state

Default state location:

```text
%LOCALAPPDATA%\CadenceStudio\update-trust\manifest-development.json
```

The state is protected against path/reparse-point escapes and is replaced
atomically. Sequence rules are:

```text
incoming < retained     => reject rollback/replay
incoming = retained     => require identical exact-manifest SHA-256
incoming > retained     => persist new trusted state
```

Expiration/clock-failure semantics and recovery-authority statement formats remain
experimental in the vNext candidate and are intentionally not invented here.

## Validation

Exit Cadence completely, including the tray process, then run:

```powershell
.\scripts\Test-Dev6.ps1
```

The dev.6 script now runs a dedicated manifest-security/replay harness before the
existing updater transaction and signed-install suites. It must prove at minimum:

- existing P-256 identity verifies exact manifest bytes;
- one-byte manifest tampering fails;
- malformed/truncated signature fails;
- pending, retired, revoked, recovery-role, and unknown identities cannot authorize
  a routine release;
- first sequence is persisted;
- exact same sequence/bytes may be rechecked;
- same sequence with different bytes fails;
- older sequence fails;
- newer sequence advances state;
- corrupt retained state fails closed;
- all dev.4/dev.5/dev.6A updater, payload-signature, health, rollback, path, and
  protocol regressions remain green.

No production private key material belongs in this repository or these tests.
