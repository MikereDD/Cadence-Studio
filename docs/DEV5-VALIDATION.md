# dev.5 validation and final audit

## Confirmed execution evidence

- GitHub `dev/v1.1` resolved to `c11c914708cf7c15d0eebe715ab8e074648bd436`. Local base matched and was clean.
- Full Windows solution build: **succeeded, 0 errors, 88 analyzer warnings**, SDK 8.0.424. This was the earlier full-solution integration build, before final documentation/audit edits.
- Original executable dev.4 harness: **51/51 passed, exit 0**.
- Isolated signed security/integration suite: **85/85 checks passed, exit 0**. Covered hashes, signature-file hash, invalid signature, unapproved key ID/fingerprint, post-main-verification tampering, exact filename/sizes, same-version/downgrade, install/restart escape, ZIP traversal/absolute/backslash/ADS/reserved/duplicate/symlink/reparse entries, signed-version relabeling, extraction/backup junctions, one-backup retention, rollback, and valid signed install/restart/replay rejection.
- Subsequent combined harness: **52 top-level checks passed, exit 0** (51 legacy checks plus signed-suite invocation), including valid replacement of changed executable bytes.
- Stronger rollback fixture added changed main-executable bytes. It exposed a concrete defect: `InvalidDataException` was not caught when detecting a target needing restoration. That failure was not dismissed or counted as a pass.
- Final audit fix explicitly catches that hash-mismatch exception. Only the affected rollback case was rerun: **4/4 checks passed, exit 0**, proving main signature verification, automatic rollback, removal/restoration of files, and restored executable SHA-256. Its fixture-core and updater projects rebuilt successfully as part of this run. The full matrix was not rerun after the user's tail-only instruction.
- Public-key provisioning helper rejected the known fixture public key under a production-looking key ID; production source remained unprovisioned.
- Final whitespace check and packaging script syntax check passed.

## Final source audit

- Production app/updater references resolve to production Core. Only test projects compile the shared sources against the test anchor. No production private key exists; the sole private PEM is `tests/SigningFixture.Core/test-only-private.pem`.
- Production trust source is intentionally empty and fails closed. No transaction/manifest/environment flag can supply a trusted key. Provisioning accepts public material only and rejects the fixture key.
- Selected ZIP and signature use exact approved GitHub release paths, bounded redirects, exact names/lengths and digest/signature checks. Updater verifies independently while holding payload bytes against mutation.
- Transaction states remain separate from cryptographic authorization. An install requires verified-release state, fresh eligible metadata, known install identity and real materials; synthetic dry runs cannot install.
- Package identity is inside the signed ZIP. Replacement paths and restart path are rooted in local installation state. Extraction rejects path escapes, special/reparse entries, reserved state paths and case-insensitive duplicates.
- Backup inventory excludes only reserved updater state, uses copied bytes with hash proof, and retains one prior installation. Failed replacement restores changed old files and removes introduced files. All installed package files are checked before restart.
- Audit fixes: explicit rollback hash-mismatch catch; BOM-free package identity output for Windows PowerShell compatibility. Harness subprocess output is now forwarded for diagnosability.
- JSON transitions and log entries are flushed; errors retain reasons. No startup-health claims, retry scheduler, production deployment, commit or push was introduced.

## Remaining manual validation

Create the encrypted real release key outside the repository, pin its public half, review and rebuild both production components, then sign/publish an actually newer release and test it in a disposable production installation. Those real-key/network/runtime steps have not been performed. Exact commands are in `UPDATER-DEV5.md`.

This increment does not prove power-loss recovery, startup health, ARM64 runtime behavior, Authenticode verification, or live production downloads. It preserves the requested dev.6 boundary. See the implementation guide for limits and manual recovery behavior.
