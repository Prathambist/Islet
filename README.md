# MusicIsland

A **Dynamic Island** for Windows. It sits at the top of your screen showing what's playing, and opens into a Now Playing card when you hover over it.

Works with Spotify, Apple Music, and web players such as YouTube, YouTube Music and Spotify Web.

<p align="center">
  <img src="docs/demo.gif" alt="MusicIsland playing a song, peeking at the next one, switching between its music views, showing a Discord notification, then cycling through its idle looks" width="630">
</p>

## Install

In PowerShell:

```powershell
irm https://raw.githubusercontent.com/Prathambist/MusicIsland/main/install.ps1 | iex
```

The installer asks before changing anything. It adds Start Menu and startup shortcuts and doesn't need .NET installed. It updates itself from then on.

To uninstall:

```powershell
irm https://raw.githubusercontent.com/Prathambist/MusicIsland/main/uninstall.ps1 | iex
```

## Features

- **Now Playing:** play, pause, skip, seek, shuffle and repeat. Album art sets the colours.
- **Live visualizer:** bars that move with the music.
- **Synced lyrics:** from [LRCLIB](https://lrclib.net).
- **Volume:** scroll over the island to change it.
- **Volume and brightness:** changes show on the island.
- **Message notifications:** from Discord, WhatsApp and Messenger.
- **Mic and camera dots:** like iPhone's.
- **Nothing-style countdown timer**
- **Idle views:** a clock and the weather when nothing is playing.
- **Like button:** saves songs to Spotify (optional).
- **Hides over full-screen apps:** games and videos.
- **Customisable:** themes, sizes and views.
- **File shelf:** drop files on the island, then drag them out anywhere later.
- **Movable:** hold the island for a second, then drag it anywhere along the top.
- **Performance modes:** Low, Medium and High.

Everything is set in **Settings**. Right-click the island or use the tray icon.

<details>
<summary>Connecting Spotify for the Like button</summary>

1. Create an app at [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) with **Web API** (needs Premium).
2. Add the redirect URI `http://127.0.0.1:5543/callback`.
3. Paste its **Client ID** into Settings and press **Connect**.

</details>

## Building

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/Prathambist/MusicIsland.git
cd MusicIsland
dotnet run --project src/MusicIsland
dotnet test
```

To release, push a tag such as `v1.6.0`. GitHub Actions builds the release and installed copies update to it.
