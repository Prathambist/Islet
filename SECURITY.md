# Security

## Reporting a problem

Please don't open a public issue for a security problem. Use GitHub's private reporting instead: the repository's **Security** tab → **Report a vulnerability**. You'll get a reply as soon as possible, and credit in the release notes if you'd like it.

## What Islet can access, and why

Islet only reads what it needs to show something on the island, and almost all of it stays on your PC.

| What | Why | How |
|---|---|---|
| What's playing | Song, artist, cover, progress, controls | Windows' media sessions API, which any app can use |
| Your speakers' output | The visualizer bars | WASAPI loopback; analysed in memory, never recorded or saved |
| Notifications | Discord, WhatsApp and Messenger messages | Windows' notification listener, **only after you allow it** when Windows asks; message text is shown, never stored or logged |
| Which apps use the mic or camera | The privacy dots | Windows' audio sessions and its privacy record in the registry |
| The window in front | Hiding over full-screen apps, staying on top | Window titles and sizes |
| Windows Do Not Disturb | Holding messages while it's on | The state Windows' own notification centre publishes |
| Your Screenshots folder | Putting new screenshots on the shelf | Watching for new files there |
| Which programs have a file open | "What's using it?" | Windows' Restart Manager, only for files you drop on it |
| The screen's pixels | The colour picker | One screenshot while you pick, kept in memory and discarded |

Islet can close or end other programs, but only when you click **Close** or **End task** in "What's using it?", and ending one asks you to confirm first.

## Network

Islet talks to these services only, all over HTTPS:

- **GitHub** (`api.github.com`, `github.com`): checking for and downloading updates
- **LRCLIB** (`lrclib.net`): synced lyrics, sending the song's title, artist, album and length
- **Apple iTunes Search** (`itunes.apple.com`): the real cover for music videos, sending the title and artist
- **Open-Meteo** (`api.open-meteo.com`, `geocoding-api.open-meteo.com`): weather, sending the city you typed or its coordinates
- **Spotify** (`accounts.spotify.com`, `api.spotify.com`): only if you connect your account for the Like button

Connecting Spotify opens a listener on `127.0.0.1:5543` (your own PC only) for the sign-in to return to, and closes it straight after.

There are no analytics, telemetry or ads.

## Secrets

- The repository contains **no** API keys, passwords or tokens. Spotify needs your own Client ID, which you enter in Settings.
- Spotify sign-in uses PKCE, so no client secret is needed. The refresh token is encrypted with Windows DPAPI, so only your Windows account can read it.
- The release signing key is a GitHub Actions secret, never in the repository.

## Updates and installs are signed

Every release zip is signed with an RSA-3072 key (SHA-256, PKCS#1 v1.5) by the release workflow. Both the in-app updater and `install.ps1` check the signature against the public key built into them before unpacking anything, and refuse a release without a valid one. Releases without a signature aren't offered as updates at all.

HTTPS protects a download on its way; the signature also protects against a release that was tampered with on GitHub itself.

## Automated checks

On every push and pull request:

- **CodeQL** scans the C# code for security problems ([`codeql.yml`](.github/workflows/codeql.yml))
- **Dependency audit** fails the build if any NuGet package has a known vulnerability ([`ci.yml`](.github/workflows/ci.yml))
- **Tests** cover the security-relevant logic: signature checking, update parsing, settings migration and file handling
