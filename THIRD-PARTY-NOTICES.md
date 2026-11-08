# Third-party notices

Islet uses the following third-party software, services and assets. Their licences are listed here so they can be checked before using or redistributing Islet.

## Libraries shipped with Islet

| Package | Version | Licence | Copyright |
|---|---|---|---|
| [NAudio.Wasapi](https://github.com/naudio/NAudio) | 2.2.1 | MIT | © Mark Heath |
| [NAudio.Core](https://github.com/naudio/NAudio) (used by NAudio.Wasapi) | 2.2.1 | MIT | © Mark Heath |
| [System.Management](https://github.com/dotnet/runtime) | 10.0.14 | MIT | © Microsoft Corporation |
| [.NET runtime, WPF and Windows Forms](https://github.com/dotnet) | 10.0 | MIT | © .NET Foundation and contributors |

Releases are self-contained, so the .NET runtime is included in `Islet.exe`. Its own third-party notices are at [github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT).

## Used only to build and test (not shipped)

| Package | Version | Licence | Copyright |
|---|---|---|---|
| [Microsoft.NETFramework.ReferenceAssemblies](https://github.com/Microsoft/dotnet) | 1.0.3 | MIT | © Microsoft Corporation |
| [xunit](https://github.com/xunit/xunit) | 2.9.2 | Apache-2.0 | © .NET Foundation |
| [xunit.runner.visualstudio](https://github.com/xunit/visualstudio.xunit) | 2.8.2 | Apache-2.0 | © .NET Foundation |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | 17.11.1 | MIT | © Microsoft Corporation |
| [coverlet.collector](https://github.com/coverlet-coverage/coverlet) | 6.0.2 | MIT | © coverlet contributors |

## Web services

Islet calls these services at run time. Each has its own terms, which apply to how Islet is used:

| Service | Used for | Terms |
|---|---|---|
| [LRCLIB](https://lrclib.net) | Synced lyrics | Free, open API. Lyrics themselves are copyrighted by their rights holders. |
| [Apple iTunes Search API](https://performance-partners.apple.com/search-api) | Covers for music videos | Apple's terms for the Search API |
| [Open-Meteo](https://open-meteo.com) | Weather | Free for non-commercial use; data under CC BY 4.0. Commercial use needs a paid plan. |
| [Spotify Web API](https://developer.spotify.com) | The optional Like button | Spotify Developer Terms; each user connects their own developer app |
| [GitHub Releases](https://github.com) | Updates | GitHub Terms of Service |

## Fonts, icons and marks

- **Segoe Fluent Icons**, **Segoe MDL2 Assets**, **Segoe UI** and **Cascadia Mono** are Windows system fonts. Islet uses them from Windows and doesn't include them.
- The **Discord**, **WhatsApp** and **Messenger** logos drawn on message notifications are trademarks of Discord Inc. and Meta Platforms, Inc., used only to show which app a message came from.
- **Spotify** and **Apple Music** are trademarks of Spotify AB and Apple Inc. "Dynamic Island" is a name used by Apple; Islet isn't affiliated with or endorsed by Apple.
- The Islet app icon (`src/Islet/Assets/Islet.ico`) and the demo animation (`docs/demo.gif`) were made for this project.
