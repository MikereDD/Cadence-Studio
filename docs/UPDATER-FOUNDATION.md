# Cadence Studio Updater Foundation

Cadence Studio follows the Typezer∅ Release Standards repository as the canonical updater contract.

## v1.1-dev.4 extension

The discovery behavior below is preserved. See [UPDATER-TRANSACTIONS.md](UPDATER-TRANSACTIONS.md) for the separate dry-run updater, transaction/staging contract, limitations and exact build/runtime tests.

## v1.1-dev.3 scope (historical discovery baseline)

This increment implements **discovery and eligibility only**.

The application may:

1. perform a user-initiated update check;
2. contact the single approved Cadence Studio manifest origin over HTTPS;
3. cap manifest size and bound network time;
4. deserialize with unknown JSON properties rejected;
5. validate release-manifest schema revision 2;
6. validate Cadence application identity, Windows platform, architecture, and channel;
7. parse and numerically compare Stable and Development version forms;
8. validate updater protocol compatibility;
9. validate the exact canonical updater payload filename and metadata;
10. validate detached-signature metadata shape and release-key fingerprint metadata shape;
11. validate source repository/tag/commit metadata;
12. validate the Windows one-version rollback policy;
13. report whether the candidate is current, available, incompatible, or invalid.

## Approved manifest endpoint

The endpoint is compiled into Cadence Studio:

```text
https://raw.githubusercontent.com/MikereDD/Cadence-Studio/main/updates/<channel>/release-manifest.json
```

For v1.1 development builds, `<channel>` is `development`.

The runtime additionally verifies that the URI exactly matches the expected HTTPS host and repository path before making the request.

## Security boundary

A manifest saying that a payload is signed is not proof that the payload is signed.

v1.1-dev.3 validates manifest metadata only. Later updater increments must still:

- download to randomized application-owned staging;
- verify exact payload name and size;
- calculate and verify payload SHA-256;
- calculate and verify signature-file SHA-256;
- verify the detached signature using a locally pinned release key;
- independently repeat verification in the separate updater process;
- validate all install, source, target, backup, and restart paths;
- safely extract without traversal, reparse-point, or symlink escape;
- replace files only after the main application exits;
- prove critical installed hashes after replacement;
- perform bounded startup-health confirmation;
- retain one known-good version and roll back once when appropriate.

No remote manifest may introduce or replace the locally trusted release key merely by declaring new key metadata.

## Current protocol

```text
Manifest schema: 2
Updater protocol: 2
```

The client rejects a release whose `minimumUpdaterProtocolVersion` is newer than the locally compiled updater protocol.
