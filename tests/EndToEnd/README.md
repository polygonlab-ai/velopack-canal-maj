# End-to-end test (manual, Windows)

The unit tests replace Velopack with a fake. This test checks the real chain once, on a Windows machine:
build, pack, publish, local web server, installation, update.

It is optional and never runs in CI: it installs an application and needs someone to click.

## What it does

```
build/pack.ps1 1.0.0 ──► build/publish.ps1 ──► artifacts/e2e/server/sampleapp-e2e/ ◄── dotnet-serve (http://localhost:8080)
                                                                                           │
SampleAppE2E-stable-Setup.exe ──► installs 1.0.0 ──► "Check for updates" ─────────────────┘
build/pack.ps1 1.0.1 ──► build/publish.ps1 ──► same folder ──► "Restart to update" ──► 1.0.1
```

The test application has its own id (`SampleAppE2E`) and talks to `http://localhost:8080/sampleapp-e2e/` only:
it never touches a real installation or a real server. Plain HTTP is accepted because the address is `localhost`.

## Run it

Requirements: Windows 10 or 11 (x64), the .NET 10 SDK, PowerShell 7.

```powershell
./tests/EndToEnd/run-e2e.ps1
```

The script stops and asks two questions:

1. after the installation: does SampleApp show **Installed version 1.0.0**?
2. after the update: does SampleApp show **Installed version 1.0.1**?

Answer `y` when the window shows what is asked. Any other answer stops the test.

Options: `-Port 8090` if port 8080 is busy, `-Channel beta` to run the same test on the beta channel.

## Without the script

Any static web server works in place of dotnet-serve, for example:

```powershell
./build/pack.ps1 -Version 1.0.0 -PackId SampleAppE2E -FeedUrl http://localhost:8080/sampleapp-e2e/ -OutputRoot artifacts/e2e/releases
./build/publish.ps1 -SourceRoot artifacts/e2e/releases -Destination artifacts/e2e/server/sampleapp-e2e
python -m http.server 8080 --directory artifacts/e2e/server
```

Then run `artifacts/e2e/server/sampleapp-e2e/SampleAppE2E-stable-Setup.exe`, pack and publish `1.0.1` the same way,
and use the two buttons of the window.

## Clean up

Uninstall **SampleApp (end-to-end test)** from Settings > Apps, and delete `artifacts/e2e/`.
