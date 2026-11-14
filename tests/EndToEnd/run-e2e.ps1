#Requires -Version 7.2
<#
.SYNOPSIS
    Manual end-to-end test: install version 1.0.0 from a local update server, then update it to 1.0.1.

.DESCRIPTION
    Runs on Windows, by hand; never in CI. Nothing leaves the machine: the update server is a local folder
    served on http://localhost:<port>/ by dotnet-serve (pinned in .config/dotnet-tools.json).

    The test application gets its own id (SampleAppE2E), so it never touches a real SampleApp installation.
    Steps:
      1. pack 1.0.0 with FeedUrl = http://localhost:<port>/sampleapp-e2e/ and publish it to the local folder;
      2. start the local server;
      3. run the installer: SampleApp opens and shows version 1.0.0;
      4. pack and publish 1.0.1;
      5. in SampleApp, click "Check for updates", then "Restart to update": the window shows 1.0.1.
    See tests/EndToEnd/README.md.

.EXAMPLE
    ./tests/EndToEnd/run-e2e.ps1
#>
[CmdletBinding()]
param(
    # Local port of the test update server.
    [ValidateRange(1024, 65535)]
    [int] $Port = 8080,

    # Channel used by the test.
    [ValidatePattern('^[a-z][a-z0-9-]{0,31}$')]
    [string] $Channel = 'stable'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'The end-to-end test installs the application: run it on Windows.'
}

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$work = Join-Path $root 'artifacts/e2e'
$releases = Join-Path $work 'releases'
$served = Join-Path $work 'server'
$appFolder = Join-Path $served 'sampleapp-e2e'
$feedUrl = "http://localhost:$Port/sampleapp-e2e/"
$packId = 'SampleAppE2E'

function Step([string] $Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }
function Confirm-Step([string] $Question) {
    $answer = Read-Host "$Question [y/N]"
    if ($answer -notmatch '^(y|yes)$') { throw "Stopped: '$Question' answered '$answer'." }
}
function Pack([string] $Version) {
    & (Join-Path $root 'build/pack.ps1') -Version $Version -Channel $Channel -FeedUrl $feedUrl `
        -PackId $packId -PackTitle 'SampleApp (end-to-end test)' -OutputRoot $releases
}
function Publish() {
    & (Join-Path $root 'build/publish.ps1') -Channel $Channel -SourceRoot $releases -Destination $appFolder
}

if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force
}

$server = $null
try {
    Step '1/5 Pack and publish version 1.0.0'
    Pack '1.0.0'
    Publish

    Step "2/5 Start the local update server on $feedUrl"
    dotnet tool restore | Out-Null
    # The server runs in its own PowerShell window, so its log stays visible; it is stopped at the end.
    $quoted = "'" + $served.Replace("'", "''") + "'"
    $serve = "Set-Location '$($root.Replace("'", "''"))'; dotnet tool run dotnet-serve --directory $quoted --port $Port --mime .nupkg=application/octet-stream"
    $server = Start-Process -FilePath 'pwsh' -PassThru -ArgumentList @('-NoProfile', '-Command', $serve)
    Start-Sleep -Seconds 3
    $index = Invoke-WebRequest -Uri ($feedUrl + "releases.$Channel.json") -UseBasicParsing
    Write-Host "    releases.$Channel.json served (HTTP $($index.StatusCode))"

    Step '3/5 Install version 1.0.0'
    $setup = Join-Path $appFolder "$packId-$Channel-Setup.exe"
    Start-Process -FilePath $setup -Wait
    Confirm-Step 'Did SampleApp open, showing "Installed version 1.0.0" and the channel?'

    Step '4/5 Pack and publish version 1.0.1'
    Pack '1.0.1'
    Publish

    Step '5/5 Update'
    Write-Host '    In SampleApp: click "Check for updates", wait for "Version 1.0.1 is ready", then click "Restart to update".'
    Confirm-Step 'Did SampleApp restart, showing "Installed version 1.0.1"?'

    Write-Host ''
    Write-Host 'End-to-end test passed.' -ForegroundColor Green
    Write-Host "Uninstall 'SampleApp (end-to-end test)' from Settings > Apps when you are done."
}
finally {
    if ($null -ne $server -and -not $server.HasExited) {
        # /T stops the server window and the dotnet-serve process it started.
        taskkill.exe /PID $server.Id /T /F | Out-Null
    }
}
