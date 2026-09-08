# Cadence Studio v1.1-dev.5 overlay

Base: GitHub `MikereDD/Cadence-Studio`, branch `dev/v1.1`, commit
`c11c914708cf7c15d0eebe715ab8e074648bd436` (verified remotely before implementation).
This overlay contains changed/new repository-relative source files only. It does not contain a Git directory, binaries, build outputs, a production private key, or a production trust anchor. Nothing was committed or pushed.

## Apply and build on Windows

Use a clean checkout at the base commit. Extract the overlay at the repository root, so `src`, `tests`, `updater`, `scripts`, and `docs` merge with those directories.

```powershell
git switch dev/v1.1
git rev-parse HEAD
git status --short
# Verify HEAD is c11c914708cf7c15d0eebe715ab8e074648bd436 and the checkout is clean.
Expand-Archive -LiteralPath C:\Downloads\Cadence-Studio-v1.1-dev.5-overlay.zip -DestinationPath . -Force
dotnet build .\CadenceStudio.sln -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project .\tests\CadenceStudio.Updater.Tests\CadenceStudio.Updater.Tests.csproj -c Debug --no-build
if ($LASTEXITCODE -ne 0) { throw 'Security harness failed' }
git diff --check
git status --short
```

Windows and .NET SDK 8 are required. The executable harness, not `dotnet test`, runs the tests. It includes the original 51 checks and launches the signed suite. Alternatively run `scripts/Test-Dev5.ps1`. The standalone signed suite command is:

```powershell
dotnet run --project .\tests\CadenceStudio.SignedInstall.Tests\CadenceStudio.SignedInstall.Tests.csproj
```

Fixtures are disposable installations under `%TEMP%\CadenceStudio-dev5-<guid>`. Transactions and logs are retained under `%LOCALAPPDATA%\CadenceStudio\updates\<transaction-id>`. The harness prints its fixture directory. No real Cadence installation is used. Junction creation does not require developer-mode symlink support.

## Production key: owner action required

Production intentionally rejects every installation until a real public key is pinned. Discovery and dry runs remain available. The test key is deliberately public test material; it must never authorize a production release.

On your signing machine, use OpenSSL to create an **encrypted** ECDSA P-256 key in a private directory outside the repository and outside release/publish directories. Replace `X:\PrivateSigning` with your private storage location. OpenSSL prompts for the passphrase; do not put it on the command line.

```powershell
openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:prime256v1 -aes-256-cbc -out X:\PrivateSigning\cadence-release-private.pem
if ($LASTEXITCODE -ne 0) { throw 'Key generation failed' }
openssl pkey -in X:\PrivateSigning\cadence-release-private.pem -pubout -out X:\PrivateSigning\cadence-release-public.pem
if ($LASTEXITCODE -ne 0) { throw 'Public export failed' }
pwsh -NoProfile -File .\scripts\Set-ReleaseTrustAnchor.ps1 -PublicKeyPath X:\PrivateSigning\cadence-release-public.pem -KeyId cadence-release-2026-01
if ($LASTEXITCODE -ne 0) { throw 'Public-key provisioning failed' }
git diff -- .\src\CadenceStudio.Core\Updates\ReleaseTrustAnchor.cs
dotnet build .\CadenceStudio.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Rebuild failed' }
```

Keep the encrypted private key and its recovery backup private. The provisioning script writes only canonical public PEM, key ID and the fixed algorithm into production source; it prints the SHA-256 of canonical DER SubjectPublicKeyInfo for manifest metadata. It rejects private PEM input and the known fixture public key. Review the public-key source change and rebuild **both** application and updater. Never copy a fixture DLL or updater into the production output. There is no runtime key override, test-mode trust switch, or remote key replacement.

The initial pinned dev.5 build must be distributed through your existing trusted manual/full-installer path: dev.4 has no installation mode, and an unprovisioned build cannot trust a remotely supplied key. Key rotation likewise requires a reviewed trusted application release.

## Package and sign a release

From the provisioned checkout, publish a complete application payload. Do not ZIP the source overlay as an update payload.

```powershell
$publish = & .\packaging\Publish-CadenceStudio.ps1 -Runtime win-x64 -Deployment SelfContained -Configuration Release
$publish = [string](@($publish)[-1])
$version = (Get-Content .\VERSION -Raw).Trim()
$asset = Join-Path (Resolve-Path .\artifacts).Path "Cadence-Studio-v$version-win-x64.zip"
if (Test-Path -LiteralPath $asset) { throw 'Choose a fresh release output; do not overwrite an existing signed asset' }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $asset
openssl dgst -sha256 -sign X:\PrivateSigning\cadence-release-private.pem -out "$asset.sig" $asset
if ($LASTEXITCODE -ne 0) { throw 'Signing failed' }
openssl dgst -sha256 -verify X:\PrivateSigning\cadence-release-public.pem -signature "$asset.sig" $asset
if ($LASTEXITCODE -ne 0) { throw 'Detached verification failed' }
Get-FileHash -LiteralPath $asset,"$asset.sig" -Algorithm SHA256
Get-Item -LiteralPath $asset,"$asset.sig" | Select-Object Name,Length
```

The ZIP root must contain `CadenceStudio.exe`, `CadenceStudio.dll`, `CadenceStudio.deps.json`, `CadenceStudio.runtimeconfig.json`, all runtime/application dependencies, the `updater` directory, and `cadence-update.json`. Publishing now writes this signed identity file:

```json
{"appId":"cadence-studio","version":"1.1-dev.5","architecture":"win-x64","channel":"development"}
```

Its exact version must match the release manifest and be newer than the installed version. For a subsequent release, update `VERSION`, `ProductInfo`, and app/updater informational versions consistently before publishing. No same-version installation or downgrade is allowed.

Populate the canonical schema-2 release manifest using the Typezero standards template and the actual release metadata. In its selected asset set:

- `fileName`: exact ZIP name; `size`: actual byte length; `sha256`: actual ZIP hash.
- `downloadUrl`: `https://github.com/MikereDD/Cadence-Studio/releases/download/v<VERSION>/<EXACT-ZIP-NAME>`.
- `signature.algorithm`: `ecdsa-sha256` (OpenSSL DER ECDSA signature, not IEEE P1363).
- `signature.fileName`: exact ZIP name plus `.sig`; `size` and `sha256`: actual signature-file bytes.
- `signature.downloadUrl`: the corresponding exact GitHub release asset URL.
- `signature.keyId`: the locally provisioned ID; `publicKeySha256`: the printed DER-SPKI fingerprint.
- Windows rollback policy retains exactly one version. Keep channel, architecture, source tag, source commit, minimum application version and updater protocol fields accurate.

Upload the immutable ZIP and `.sig` with the matching tag, then publish the release manifest at `main/updates/development/release-manifest.json` through your normal release workflow. These upload steps were **not performed**. The local `release-manifest.json` written inside the publish directory is packaging information, not the remote schema-2 discovery manifest.

Only the explicitly approved GitHub release path is accepted initially; bounded HTTPS redirects to GitHub asset hosts are allowed. ZIPs are limited to 1 GiB, signatures to 16 KiB, expanded content to 2 GiB and 20,000 entries. This increment supports the pinned P-256/SHA-256 scheme only. A manifest requesting Authenticode identity verification is rejected until that independent verification feature exists; it is never silently ignored.

## Runtime verification

```powershell
& .\src\CadenceStudio.App\bin\Debug\net8.0-windows\CadenceStudio.exe
```

In About, use **Check for updates**, then **Test updater (no installation)** to retain the dev.4 behavior. **Install update and restart** requires an eligible manifest and a provisioned key. It stages and verifies materials, starts the installed updater and requests normal app shutdown. Until you pin a real key, installation reports the unprovisioned-anchor rejection and leaves installed files unchanged.

For a real install test, use a separate copy of the provisioned application and an actually newer signed release. Inspect `transaction.json` and `updater.log`. Success ends in `Restarted`, with one `.cadence-previous` directory inside that installation. A replacement failure ends in `RolledBack` if restoration succeeds. `RollbackFailed` retains the backup for manual investigation. The signed harness supplies the valid synthetic install and forced replacement failure without needing a production key or uploading a release.

## Final design and boundaries

The updater derives its install root from its own installed `updater` directory. Both processes validate metadata and signatures independently. A held read handle prevents payload writes/deletes during extraction. Directory handles deny rename/delete while bounded file operations run. ZIP traversal, absolute/ADS/device paths, duplicate case-insensitive names, reserved updater-state paths and symlink/reparse entries are rejected. Signed package identity prevents relabeling an older signed ZIP through an altered remote manifest.

The updater waits at most 30 seconds for the identified main process, holds a per-install exclusive lease, builds a verified snapshot of installed files, retains one backup, uses same-directory atomic file replacement, removes obsolete package files and verifies every installed package file before launch. Rollback restores changed prior files and removes newly introduced files. User data remains in the existing external app-data location; do not place personal data in the replaceable install directory. Empty directories can remain after rollback.

All transaction transitions include a reason and are persisted before their log entry; both JSON and log writes are flushed. No automatic retry is scheduled, and terminal transactions cannot replay. Unexpected/complex old staging directories remain available for inspection rather than being recursively cleaned by the dev.4 cleanup policy.

Startup health confirmation, automatic rollback for startup failure, crash/power-loss recovery orchestration and broader UX remain outside this increment. `Restarted` proves process creation, not healthy initialization. Full installation is a multi-file transaction, not a power-loss-atomic directory swap. If a process is forcibly interrupted during replacement, preserve the transaction and backup for manual recovery. Read-only installations fail without requesting elevation. No live production download/sign/install has been claimed or performed.

Canonical references inspected from GitHub before implementation:

- https://github.com/MikereDD/Typezero-Release-Standards/blob/main/docs/WINDOWS-UPDATER-STANDARD.md
- https://github.com/MikereDD/Typezero-Release-Standards/blob/main/docs/RELEASE-SIGNING-STANDARD.md
- https://github.com/MikereDD/Typezero-Release-Standards/blob/main/docs/CHANNEL-AND-ROLLBACK-POLICY.md
