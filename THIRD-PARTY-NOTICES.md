# Third-party components

BK DayZ Mod Downloader is derived from DepotDownloader and distributed under GPL-2.0; see LICENSE. Corresponding application and modified engine source is included in Source/BKDayzModDownloader-source.zip in the binary distribution.

## DepotDownloader

- Copyright SteamRE Team and contributors.
- Version: 3.4.0, commit c553ef4d60c00a4f5fd16c9fe017f569001589ff.
- Source: https://github.com/SteamRE/DepotDownloader
- License: GNU GPL version 2, included in LICENSE and vendor/DepotDownloader/LICENSE.
- BK changes: .NET 10 engine project; pipe-based login, cooperative pause/resume/cancel and progress events including legacy HTTP content; DPAPI session storage; fail downloads when workshop manifests/content cannot be located; validate manifest paths; reset per-download manifest state, close partial-download file handles and dispose per-job CDN clients for a persistent login session. Upstream console entry point, console authenticator, account store and terminal helpers are replaced by the BK adapters.

## Runtime libraries

- SteamKit2 3.2.0 — LGPL-2.1-only — https://github.com/SteamRE/SteamKit
- protobuf-net / protobuf-net.Core 3.2.52 — Apache-2.0 — https://github.com/protobuf-net/protobuf-net
- QRCoder 1.6.0 — MIT — https://github.com/codebude/QRCoder
- ZstdSharp.Port 0.8.5 — MIT — https://github.com/oleg-st/ZstdSharp
- Microsoft .NET libraries — MIT — https://github.com/dotnet/runtime

NuGet package metadata and license files are additionally copied into the binary package's Licenses directory by the release workflow.
