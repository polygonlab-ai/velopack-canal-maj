# Changelog

All notable changes to this repository are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-11-14 - Self-hosted update channel for Velopack apps

### Added

- `UpdateChannel` library (.NET 10): settings read from the `UpdateChannel` section of `appsettings.json`, with defaults (`stable` channel, check on startup) and clear validation (HTTPS required, plain HTTP accepted on `localhost` only, channel name checked, every problem listed at once).
- Explicit update states: idle, checking, up to date, update available, downloading with progress, ready to restart, network error, not installed by Velopack, failed; one operation at a time.
- Background check and download at startup, never blocking the user interface and never throwing, turned off with `CheckOnStartup: false`.
- Step-by-step logging through the standard `ILogger` (no log file imposed), and an adapter that sends Velopack's own messages to `ILogger`.
- Velopack isolated behind the `IUpdateEngine` interface; `VelopackUpdateEngine` reads `releases.<channel>.json` from a plain web folder.
- WPF sample application: installed version, channel, update server, "Check for updates", status and progress, "Restart to update"; `VelopackApp.Build().Run()` as the first statement of a custom `Main`.
- `build/pack.ps1` (PowerShell 7): self-contained `win-x64` publication, `vpk pack` with version and channel, release files in `artifacts/releases/<channel>/`, channel (and optional server address) written into the released `appsettings.json`, documented place for code signing.
- `build/publish.ps1`: index checked before any copy, packages first and release index last, atomic index replacement, unchanged packages skipped, folder or custom upload command (`scp`, `rsync`...), `-WhatIf`.
- Server configurations for nginx, Apache and Caddy: content types, `no-cache` for the index, installer and portable archive, one-year immutable cache for packages, hidden files refused.
- Tests (xUnit v3, Microsoft Testing Platform): settings, every update state, network errors, cancellation, application not installed; coverage of the library above 90 % (lines and branches); manual end-to-end test (install 1.0.0 from a local server, update to 1.0.1).
- Continuous integration on `windows-latest`: build with warnings as errors and .NET analyzers, `dotnet format`, tests with coverage, demo `vpk pack` kept as an artifact; Dependabot for NuGet, .NET tools and GitHub Actions.

[0.1.0]: https://github.com/polygonlab-ai/velopack-canal-maj/releases/tag/v0.1.0

---
— Conçu et développé par PolygonLab · polygonlab.ai · Licence MIT
