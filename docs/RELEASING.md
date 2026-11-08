# Releasing

Pushing a tag like `v2.1.0` runs [`release.yml`](../.github/workflows/release.yml), which tests, builds, signs and publishes the release. Installed copies pick it up within 12 hours, or at their next launch.

## One-time setup: the signing key

Releases are signed so the updater and installer can refuse anything that didn't come from you. The private key is a GitHub secret; the matching public key is built into the app and `install.ps1`.

1. Open the private key file (made with `tools/new-release-key.cs`; see below) and copy its whole contents, including the `-----BEGIN PRIVATE KEY-----` and `-----END PRIVATE KEY-----` lines.
2. On GitHub: **Settings → Secrets and variables → Actions → New repository secret**. Name it `RELEASE_SIGNING_KEY` and paste the key.
3. Keep a backup of the key somewhere safe and offline, like a password manager. Never commit it; `*.pem` is in `.gitignore` for that reason.

Without the secret, the release workflow stops at the signing step rather than publishing an unsigned release.

### If the key is lost or leaked

Make a new pair:

```powershell
dotnet run tools/new-release-key.cs -- C:\somewhere\safe\release-signing-key.pem
```

Put the printed public key in `src/Islet/Services/ReleaseSignature.cs` and `install.ps1` (a test checks they match), update the secret, and release. Copies built with the old public key can't verify releases signed with the new one, so those users need to run the install command again once.

## Checklist for each release

Before tagging:

- [ ] `main` is green: the CI and CodeQL checks pass on the last commit
- [ ] `dotnet test` passes locally
- [ ] Run a fresh `dotnet run --project src/Islet` and check the island: music card, a notification, the shelf, the colour picker, and Settings
- [ ] README describes any new features
- [ ] The version is higher than the last release (the updater ignores anything not newer)

Tag and push:

```powershell
git tag v2.1.0
git push origin v2.1.0
```

After the workflow finishes:

- [ ] The release on GitHub has three files: `Islet-win-x64.zip`, `.zip.sig` and `.zip.sha256`
- [ ] **Fresh install**: on a PC (or Windows Sandbox) without Islet, run the install command from the README; it should say "Signature checked", install, and start the island
- [ ] **Update**: on a PC with the previous version, open Settings → Advanced → Check now; it should offer the new version, install it and restart
- [ ] **Uninstall**: run the uninstall command; Islet, its shortcuts and its install folder should be gone (settings in `%AppData%\Islet` are kept)
- [ ] Settings → Advanced shows the new version number

Windows Sandbox (an optional Windows feature) is a quick way to test a fresh install on a clean system.

## Checking a download by hand

Each release includes `Islet-win-x64.zip.sha256`. To check a downloaded zip:

```powershell
(Get-FileHash .\Islet-win-x64.zip -Algorithm SHA256).Hash.ToLower()
```

and compare it with the file's contents. The signature check that the updater and installer do is stronger: it also proves the zip was made by whoever holds the signing key.
