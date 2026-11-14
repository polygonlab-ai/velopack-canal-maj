```
       ▄▄▀▀▄▄        ____       _                         _          _
    ▄▀▀  ▄▄  ▀▀▄    |  _ \ ___ | |_   _  __ _  ___  _ __ | |    __ _| |__
    █  ▄▀  ▀▄  █    | |_) / _ \| | | | |/ _` |/ _ \| '_ \| |   / _` | '_ \
    █  ▀▄  ▄▀  █    |  __/ (_) | | |_| | (_| | (_) | | | | |__| (_| | |_) |
    ▀▄▄  ▀▀  ▄▄▀    |_|   \___/|_|\__, |\__, |\___/|_| |_|_____\__,_|_.__/
       ▀▀▄▄▀▀                     |___/ |___/
                                   polygonlab.ai
```

# velopack-canal-maj

> A self-updating .NET Windows application served from your own web server: a sample WPF app, a small update library,
> pack and publish scripts, and ready-made nginx, Apache and Caddy configurations. Built on Velopack.

[![CI](https://github.com/polygonlab-ai/velopack-canal-maj/actions/workflows/ci.yml/badge.svg)](https://github.com/polygonlab-ai/velopack-canal-maj/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/polygonlab-ai/velopack-canal-maj)](https://github.com/polygonlab-ai/velopack-canal-maj/releases)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10%20LTS-blue)](global.json)

```
 DEVELOPER MACHINE                          WEB SERVER (HTTPS)                     USER'S PC
 ─────────────────                          ──────────────────                     ─────────
 build/pack.ps1                             one plain folder per application       SampleApp, installed by Setup.exe
   dotnet publish (Release, win-x64)        /srv/updates/sampleapp/
   vpk pack --channel stable                  releases.stable.json   ◄──────────── 1. GET releases.<channel>.json
        │                                     SampleApp-1.0.1-stable-full.nupkg       (at startup, or "Check for updates")
        ▼                                     SampleApp-1.0.1-stable-delta.nupkg ◄─ 2. download the delta (or full) package
 artifacts/releases/stable/                   SampleApp-stable-Setup.exe              3. "Restart to update": Velopack
        │                                     ...                                        swaps the files, restarts the app
        ▼                                            ▲
 build/publish.ps1  ─────────────────────────────────┘
   packages first, index last                nginx · Apache · Caddy (server/)
```

## Why

Velopack installs and updates Windows applications, and it can read updates from any static web folder: no app
store, no update service, no fee per download. What is left to you is the chain around it: how to build a release,
how to copy it to the server without breaking the clients that read it at the same moment, which cache headers the
server must send, how to run a beta channel next to the stable one, and what the application should show when the
network is down or when it was started from the build folder. This repository answers each of these questions with
working code, tests and the pitfalls we met.

## Features

- WPF sample application: installed version, channel, "Check for updates", status, "Restart to update".
- `UpdateChannel` library: validated settings from `appsettings.json` (HTTPS required, `localhost` allowed for tests).
- Explicit update states: up to date, update available, downloading, ready to restart, network error, not installed, failed.
- Background check at startup, never blocking the window, and easy to turn off.
- Step-by-step logging through the standard `ILogger`; no log file imposed.
- `build/pack.ps1`: `dotnet publish` (self-contained, `win-x64`) and `vpk pack`, one folder per channel.
- `build/publish.ps1`: packages first, release index last, atomic index replacement, `-WhatIf`.
- Server configurations for nginx, Apache and Caddy: content types, no cache for the index, long cache for packages.
- Channels `stable` and `beta` side by side in one server folder, published independently.
- Delta updates out of the box (Velopack): 1.0.0 to 1.0.1 of the sample downloads about 140 KB instead of 64 MB.

## Installation

Requirements: Windows 10 or 11 (x64), the .NET 10 SDK,
PowerShell 7. The `vpk` tool is restored by the scripts from `.config/dotnet-tools.json`.

```powershell
git clone https://github.com/polygonlab-ai/velopack-canal-maj.git
cd velopack-canal-maj
dotnet build UpdateChannel.slnx
```

## Quick start

Build release `1.0.0` of the sample on the `stable` channel, then preview its publication to a folder:

```powershell
./build/pack.ps1 -Version 1.0.0
./build/publish.ps1 -Channel stable -Destination C:\updates\sampleapp -WhatIf
```

Expected output of `pack.ps1` (sizes depend on the .NET version):

```text
==> Release files in C:\src\velopack-canal-maj\artifacts\releases\stable
    RELEASES-stable                                             1 KB
    SampleApp-1.0.0-stable-full.nupkg                      64,370 KB
    SampleApp-stable-Portable.zip                          64,369 KB
    SampleApp-stable-Setup.exe                             71,709 KB
    assets.stable.json                                          1 KB
    releases.stable.json                                        1 KB
Done. Next step: ./build/publish.ps1 -Channel stable -Destination <folder>
```

`publish.ps1 -WhatIf` lists the six copies in their order (packages first, `releases.stable.json` last) and copies nothing. To see a real installation and update
on your machine, with a local web server, run the end-to-end test: [tests/EndToEnd/README.md](tests/EndToEnd/README.md).

## Usage

### 1. Point the application to your server

`src/SampleApp/appsettings.json`:

```json
{
  "UpdateChannel": {
    "FeedUrl": "https://updates.example.com/sampleapp/",
    "Channel": "stable",
    "CheckOnStartup": true,
    "AllowDowngrade": false
  }
}
```

`build/pack.ps1` writes the channel it packs (and `-FeedUrl`, if given) into the `appsettings.json` of the release,
so an application packed for `beta` always follows `beta`. The source file is never modified.

### 2. Build a release

```powershell
./build/pack.ps1 -Version 1.1.0 -ReleaseNotes .\notes\1.1.0.md
```

Keep `artifacts/releases/<channel>/` between releases: `vpk pack` builds the delta package from the previous full
package it finds there. On a new machine, fetch the current release from the server first:

```powershell
dotnet tool restore
dotnet tool run vpk download http --url https://updates.example.com/sampleapp/ --channel stable --outputDir artifacts/releases/stable
```

### 3. Publish it

To a folder, a mapped drive or a network share:

```powershell
./build/publish.ps1 -Channel stable -Destination \\fileserver\updates\sampleapp
```

With your own transfer command, run once per file, in the right order (`updates-server` is a host of your SSH config):

```powershell
./build/publish.ps1 -Channel stable -UploadCommand { param($File) scp $File updates-server:/srv/updates/sampleapp/ }
./build/publish.ps1 -Channel stable -UploadCommand { param($File) rsync --checksum $File updates-server:/srv/updates/sampleapp/ }
```

Before copying anything, `publish.ps1` checks that every package listed in `releases.<channel>.json` is present.
It then copies the packages, the installer and portable archive, and the index files last. With `-Destination`,
unchanged packages are skipped and each index file is written under a temporary name, then renamed.

### 4. Serve the folder

Pick the configuration of your web server in [`server/`](server/), adapt the host name, certificate and folder:

| File | Server | Check the configuration |
|---|---|---|
| [`server/nginx.conf`](server/nginx.conf) | nginx 1.24 or later | `nginx -t` |
| [`server/apache.conf`](server/apache.conf) | Apache httpd 2.4 (`mod_ssl`, `mod_headers`, `mod_mime`) | `apachectl configtest` |
| [`server/Caddyfile`](server/Caddyfile) | Caddy 2 (certificate obtained automatically) | `caddy validate` |

All three send the same headers:

| Files | `Cache-Control` | Why |
|---|---|---|
| `releases.<channel>.json`, `RELEASES-<channel>`, `assets.<channel>.json` | `no-cache` | Rewritten at every release; a cached copy hides the new version |
| `<id>-<channel>-Setup.exe`, `<id>-<channel>-Portable.zip` | `no-cache` | Same name at every release |
| `<id>-<version>-<channel>-full.nupkg`, `-delta.nupkg` | `public, max-age=31536000, immutable` | The version is in the name: a published package never changes |

They also return 404 (or 403) for hidden files, such as the temporary index of `publish.ps1`, and list no directory.

### 5. The published folder, file by file

After `1.0.0` and `1.0.1` on `stable`, and `1.1.0-beta.1` on `beta`, `/srv/updates/sampleapp/` holds:

| File | Read by | Role |
|---|---|---|
| `releases.stable.json` | the installed application | Index of the channel: every package with its version, type, size, SHA-1 and SHA-256. Read first, published last |
| `RELEASES-stable` | older update clients | The same list in the legacy text format |
| `assets.stable.json` | tools only | Files created by the last `vpk pack` run |
| `SampleApp-1.0.1-stable-full.nupkg` | the application | Complete version 1.0.1; used when no delta applies |
| `SampleApp-1.0.1-stable-delta.nupkg` | the application | Only the differences from 1.0.0 |
| `SampleApp-1.0.0-stable-full.nupkg` | the application | Previous version, still listed in the index: keep it |
| `SampleApp-stable-Setup.exe` | new users | Installer of the latest stable version |
| `SampleApp-stable-Portable.zip` | new users | Portable copy (no installation) that can still update itself |
| `releases.beta.json`, `RELEASES-beta`, `assets.beta.json`, `SampleApp-1.1.0-beta.1-beta-full.nupkg`, `SampleApp-beta-Setup.exe`, `SampleApp-beta-Portable.zip` | beta users | The same set for the `beta` channel |

The application requests `releases.<channel>.json` with a query string (`?arch=...&os=...&rid=...`): it is
informational and a static server ignores it.

### 6. Channels: stable and beta

Every file name carries the channel, so both channels live in the same folder and never share a file:

```powershell
./build/pack.ps1 -Version 1.1.0-beta.1 -Channel beta
./build/publish.ps1 -Channel beta -Destination \\fileserver\updates\sampleapp     # stable files untouched
```

- Beta testers install `SampleApp-beta-Setup.exe`; everyone else installs `SampleApp-stable-Setup.exe`.
- An installed application follows the channel written in its `appsettings.json` by `pack.ps1`.
- Moving a beta tester back to stable: if the stable version is older than the installed beta, the application
  needs `AllowDowngrade: true`, otherwise it reports "up to date". The simplest way is to reinstall from the stable
  installer.
- `publish.ps1` refuses a source folder that contains a file without the channel in its name.

### 7. Use the library in your own application

1. Reference `src/UpdateChannel` (or copy its nine files) and add the `Velopack` package.
2. Call `VelopackApp.Build().Run()` as the first statement of `Main`. For WPF, write your own `Main` and set
   `<StartupObject>` (see [`src/SampleApp/Program.cs`](src/SampleApp/Program.cs)). `vpk pack` refuses an executable
   without this call.
3. Wire the service:

```csharp
var options = UpdateChannelOptions.FromConfiguration(configuration);   // throws a clear message if invalid
using var updates = new UpdateService(new VelopackUpdateEngine(options), options, logger);
updates.StatusChanged += (_, status) => ShowOnUiThread(status.Message); // raised on a background thread
_ = updates.StartupCheckAsync();                                         // returns at once, never throws

// "Check for updates" button
var status = await updates.CheckAndDownloadAsync();
// "Restart to update" button, when status.CanRestartToUpdate is true
updates.RestartToUpdate();
```

| `UpdateState` | Meaning | `Message` shown by the sample |
|---|---|---|
| `Idle` | Nothing checked yet | Updates have not been checked yet. |
| `Checking` | Index being downloaded | Checking for updates on the stable channel... |
| `UpToDate` | Latest version of the channel | You have the latest version of the stable channel. |
| `UpdateAvailable` | Newer version found | Version 1.0.1 is available. |
| `Downloading` | Package being downloaded (`Progress` 0 to 100) | Downloading version 1.0.1... 40 % |
| `ReadyToRestart` | Downloaded and verified | Version 1.0.1 is ready. Restart the application to update. |
| `NetworkError` | No network, DNS, TLS, timeout or HTTP error | The update server could not be reached. Check the network connection and try again. |
| `NotInstalled` | Not installed by Velopack | This copy was not installed by Velopack (for example, it runs from the build folder): updates are disabled. |
| `Failed` | Anything else (corrupted package, update already running...) | The update check failed: ... |

### 8. Code signing

**Why.** Windows SmartScreen warns about an unsigned installer downloaded from the internet ("Windows protected
your PC"), and antivirus products distrust unsigned programs that rewrite their own files, which is exactly what an
updater does. A signature also proves to the user who published the program. HTTPS protects the transfer, and
Velopack checks the SHA-256 of every package against the index; signing adds the publisher's identity.

**With what.** A code signing certificate from a certificate authority, whose private key lives on a hardware token
or in a cloud key vault (required for new certificates since 2023), or Azure Trusted Signing, which signs without
any certificate on the build machine.

**Where.** In `build/pack.ps1`, at the block marked "Code signing": `vpk pack` signs the application files and the
installer while it packs. No other script changes. For a certificate usable by `signtool`:

```powershell
./build/pack.ps1 -Version 1.1.0 -SignParams '/a /fd sha256 /td sha256 /tr <timestamp server of your certificate authority>'
```

For Azure Trusted Signing or another tool, replace `--signParams` in the script by `--azureTrustedSignFile` or
`--signTemplate` (one of the three; see `dotnet tool run vpk pack -H` on Windows). No certificate is shipped with this
repository and none is needed to build, test or try it.

## Configuration

`appsettings.json`, section `UpdateChannel`:

| Option | Type | Default value | Role |
|---|---|---|---|
| `FeedUrl` | string (absolute URL) | none, required | Folder of the release files. HTTPS, or HTTP on `localhost` for tests. No query string, fragment or credentials |
| `Channel` | string | `stable` | Release channel: lowercase letters, digits and hyphens, starting with a letter, 32 characters at most |
| `CheckOnStartup` | bool | `true` | Check (and download) in the background when the application starts |
| `AllowDowngrade` | bool | `false` | Accept a lower version, to leave a beta for an older stable version |

`build/pack.ps1`:

| Parameter | Default value | Role |
|---|---|---|
| `-Version` | required | SemVer 2 version, for example `1.2.0` or `1.3.0-beta.1` |
| `-Channel` | `stable` | Channel, also written into the published `appsettings.json` |
| `-FeedUrl` | value of `appsettings.json` | Server folder written into the published `appsettings.json` |
| `-Project` | `src/SampleApp/SampleApp.csproj` | Application to publish |
| `-PackId` | `SampleApp` | Unique id of the application; never change it after the first release |
| `-PackTitle` | `SampleApp` | Name shown by the installer and in Settings > Apps |
| `-PackAuthors` | empty | Publisher shown by the installer |
| `-MainExe` | `SampleApp.exe` | Main executable |
| `-Runtime` | `win-x64` | .NET runtime identifier |
| `-ReleaseNotes` | none | Markdown file of release notes |
| `-SignParams` | none | `signtool` parameters (see Code signing) |
| `-OutputRoot` | `artifacts/releases` | Root of the per-channel release folders |

`build/publish.ps1`:

| Parameter | Default value | Role |
|---|---|---|
| `-Channel` | `stable` | Channel to publish |
| `-Destination` | one of the two is required | Local folder, mapped drive or network share |
| `-UploadCommand` | one of the two is required | Script block run for each file, receiving its full path |
| `-SourceRoot` | `artifacts/releases` | Root of the per-channel release folders |
| `-WhatIf` | off | Show what would be copied, copy nothing |

## Pitfalls

These are the traps we met while putting the chain together.

- **Publication order.** The application reads `releases.<channel>.json` and then downloads the package it names.
  If the index arrives on the server before the package, a user who checks at that moment gets a 404 and an error.
  `publish.ps1` always copies the packages first and the index last, and refuses to start if a package is missing.
- **Cached index.** A proxy or CDN that caches `releases.<channel>.json` hides new versions, or keeps serving an index
  whose packages it no longer has. The index must be sent with `Cache-Control: no-cache`; packages, whose names carry
  their version, can be cached for a year. The configurations in `server/` do both.
- **Application started from the build folder.** Run from Visual Studio or `bin\Debug`, the application is not
  installed by Velopack: it cannot update itself and shows `NotInstalled` instead of failing. To try updates, install
  it with `Setup.exe` (see the end-to-end test).
- **`VelopackApp.Build().Run()` not first.** Velopack starts the application with special arguments while it installs
  or updates it. If a window opens before `Run()`, those hooks misbehave. WPF generates its own `Main`: replace it
  (`<StartupObject>`), as `Program.cs` does.
- **Antivirus and SmartScreen.** An unsigned `Setup.exe` triggers SmartScreen, and some antivirus products quarantine
  unsigned self-updating programs. Sign every public release (see Code signing).
- **Wrong channel after packing.** An application packed for `beta` with `"Channel": "stable"` in its settings would
  follow `stable`. `pack.ps1` writes the packed channel into the release to rule this out.
- **Lost release folder.** Without the previous full package in `artifacts/releases/<channel>/`, `vpk pack` cannot
  build a delta and users download the full package. Fetch it back with `vpk download http` (see Usage).
- **Packing from Linux or macOS.** `pack.ps1` cross-compiles with `vpk [win]`. It works for tests, but signing is not
  available and the delta step needs the `zstd` command. Build public releases on Windows.

## Compatibility

| Component | Version |
|---|---|
| .NET | 10 (LTS), SDK pinned by `global.json` |
| Velopack (library and `vpk`) | 1.2.161 |
| Windows (sample application) | 10 and 11, x64 |
| PowerShell (scripts) | 7.2 or later |
| Continuous integration | `windows-latest`: build, format, tests, demo `vpk pack` |

The `UpdateChannel` library targets `net10.0` and its tests run on Windows, Linux and macOS.

## Tests

```powershell
dotnet test --solution UpdateChannel.slnx --coverlet --coverlet-threshold 90 --coverlet-threshold-type line,branch --coverlet-threshold-stat Total
dotnet format UpdateChannel.slnx --verify-no-changes
```

The unit tests (xUnit v3) cover settings, every update state, network errors, cancellation and an application
started outside a Velopack installation. Velopack sits behind the `IUpdateEngine` interface, so the tests touch
neither the network nor the disk. The build treats every warning as an error, with the .NET analyzers on.

A manual end-to-end test installs 1.0.0 from a local server and updates it to 1.0.1:
[tests/EndToEnd/README.md](tests/EndToEnd/README.md).

## Limits and roadmap

Limits of this version:

- Windows only: Velopack also supports macOS and Linux, but the sample and the scripts target Windows.
- Old packages stay on the server: `publish.ps1` never deletes. Remove old full packages by hand, keeping those
  listed in `releases.<channel>.json`.
- The channel is chosen when packing; the sample has no setting to switch channel from its window.
- `-UploadCommand` sends every file, even unchanged ones; use `rsync --checksum` to skip them.
- No staged rollout: all users of a channel see a new version at the same time.

Roadmap, not in this version:

- Automated signing in the continuous integration (Azure Trusted Signing).
- Publication to object storage (S3-compatible or Azure Blob), keeping the "packages first, index last" order.
- Clean-up of old releases on the server.
- Channel switch from the application, and a NuGet package of the `UpdateChannel` library.

Delta updates are not on the list: Velopack already builds and applies them.

## Contributing

See [CONTRIBUTING.md](https://github.com/polygonlab-ai/.github/blob/main/CONTRIBUTING.md).

## Security

Report a vulnerability privately, never in a public issue: see
[SECURITY.md](https://github.com/polygonlab-ai/.github/blob/main/SECURITY.md).

## License

MIT License: see [LICENSE](LICENSE).

---
— Conçu et développé par PolygonLab · polygonlab.ai · Licence MIT
