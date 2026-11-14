#Requires -Version 7.2
<#
.SYNOPSIS
    Builds a Velopack release of the application for one channel.

.DESCRIPTION
    1. Restores the vpk tool pinned in .config/dotnet-tools.json.
    2. Publishes the application in Release, self-contained, for the chosen runtime (win-x64 by default).
    3. Runs "vpk pack" with the version and the channel.
    4. Leaves the release files in artifacts/releases/<channel>/, ready for build/publish.ps1.

    Keep artifacts/releases/<channel>/ between releases: vpk reads the previous full package there to build
    the delta package (the small download for users of the previous version).

    The script stops at the first error. It contains no company-specific value: pass yours as parameters.

.EXAMPLE
    ./build/pack.ps1 -Version 1.0.0

.EXAMPLE
    ./build/pack.ps1 -Version 1.1.0-beta.1 -Channel beta -ReleaseNotes ./notes/1.1.0-beta.1.md
#>
[CmdletBinding()]
param(
    # Release version, SemVer 2 (three numbers, optional pre-release label): 1.2.0, 1.3.0-beta.1.
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$')]
    [string] $Version,

    # Release channel: lowercase letters, digits and hyphens, starting with a letter (stable, beta...).
    # It is also written into the published appsettings.json, so that the installed application follows it.
    [ValidatePattern('^[a-z][a-z0-9-]{0,31}$')]
    [string] $Channel = 'stable',

    # Address of the update folder written into the published appsettings.json (optional; by default the value
    # of src/SampleApp/appsettings.json is kept). HTTPS, or HTTP on localhost for tests.
    [ValidatePattern('^(https://[^/?#\s]+|http://(localhost|127\.0\.0\.1)(:\d+)?)(/[^?#\s]*)?$')]
    [string] $FeedUrl = '',

    # Application project to publish.
    [string] $Project = 'src/SampleApp/SampleApp.csproj',

    # Unique id of the application for Velopack (also the prefix of every release file name). Never change it after the first release.
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string] $PackId = 'SampleApp',

    # Name shown by the installer and in the Windows "Apps" list.
    [string] $PackTitle = 'SampleApp',

    # Authors or company shown by the installer (optional).
    [string] $PackAuthors = '',

    # File name of the main executable, inside the published folder.
    [string] $MainExe = 'SampleApp.exe',

    # .NET runtime identifier of the published application.
    [string] $Runtime = 'win-x64',

    # Markdown file with the release notes of this version (optional).
    [string] $ReleaseNotes = '',

    # Parameters for signtool, to sign the application and the installer (optional, see "Code signing" in README.md).
    [string] $SignParams = '',

    # Root of the release folders; each channel gets its own sub-folder.
    [string] $OutputRoot = 'artifacts/releases'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string] $Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Command([string] $CommandLine) {
    Write-Host "    $CommandLine" -ForegroundColor DarkGray
}

# A failing native command does not stop PowerShell: check its exit code right after it.
function Assert-Success([string] $Description) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed (exit code $LASTEXITCODE)."
    }
}

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $projectPath = Join-Path $root $Project
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Project not found: $Project"
    }
    if ($ReleaseNotes -and -not (Test-Path -LiteralPath $ReleaseNotes -PathType Leaf)) {
        throw "Release notes file not found: $ReleaseNotes"
    }

    $publishDir = Join-Path $root "artifacts/publish/$Channel/$Version"
    $releaseDir = Join-Path $root (Join-Path $OutputRoot $Channel)

    Write-Host "Packing $PackId $Version for the '$Channel' channel ($Runtime)"
    Write-Host "  publish folder : $publishDir"
    Write-Host "  release folder : $releaseDir"

    Write-Step '1/3 Restoring the vpk tool (.config/dotnet-tools.json)'
    Write-Command 'dotnet tool restore'
    dotnet tool restore
    Assert-Success 'dotnet tool restore'

    Write-Step "2/3 Publishing $Project (Release, self-contained, $Runtime)"
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }
    $publishArguments = @(
        $projectPath,
        '--configuration', 'Release',
        '--runtime', $Runtime,
        '--self-contained', 'true',
        '--output', $publishDir,
        "-p:Version=$Version"
    )
    Write-Command ('dotnet publish ' + ($publishArguments -join ' '))
    dotnet publish @publishArguments
    Assert-Success 'dotnet publish'
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $MainExe) -PathType Leaf)) {
        throw "Main executable '$MainExe' not found in $publishDir (check -MainExe)."
    }

    # The installed application must follow the channel it was packed for (and, if given, the server address):
    # both are written into the published appsettings.json, never into the source file.
    $settingsPath = Join-Path $publishDir 'appsettings.json'
    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -AsHashtable
        if (-not $settings.ContainsKey('UpdateChannel')) {
            $settings['UpdateChannel'] = [ordered]@{}
        }
        $settings['UpdateChannel']['Channel'] = $Channel
        if ($FeedUrl) {
            $settings['UpdateChannel']['FeedUrl'] = $FeedUrl
        }
        $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding utf8NoBOM
        Write-Host "    appsettings.json: UpdateChannel:Channel = $Channel; FeedUrl = $($settings['UpdateChannel']['FeedUrl'])"
    }
    elseif ($FeedUrl) {
        throw "-FeedUrl was given but the published folder has no appsettings.json."
    }

    Write-Step "3/3 Creating the release with vpk pack (channel '$Channel')"
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
    $packArguments = @()
    if (-not $IsWindows) {
        # Cross-compiling from Linux or macOS: vpk needs the [win] directive. Fine for tests;
        # sign and ship real releases from Windows.
        Write-Warning 'Not running on Windows: cross-compiling the Windows release (code signing is not available).'
        $packArguments += '[win]'
    }
    $packArguments += @(
        'pack',
        '--packId', $PackId,
        '--packVersion', $Version,
        '--packDir', $publishDir,
        '--mainExe', $MainExe,
        '--packTitle', $PackTitle,
        '--channel', $Channel,
        '--runtime', $Runtime,
        '--outputDir', $releaseDir
    )
    if ($PackAuthors) {
        $packArguments += @('--packAuthors', $PackAuthors)
    }
    if ($ReleaseNotes) {
        $packArguments += @('--releaseNotes', (Resolve-Path -LiteralPath $ReleaseNotes).Path)
    }

    # ---- Code signing (optional) -------------------------------------------------------------------
    # Signing is where a certificate comes in: vpk signs every executable of the package and the
    # installer while it packs, so this is the only place to change. Pick ONE of the vpk options:
    #   --signParams "<signtool parameters>"  certificate in the Windows store or on a hardware token
    #   --azureTrustedSignFile <metadata.json> Azure Trusted Signing, no certificate on the build machine
    #   --signTemplate "<command>"             any other signing tool (see vpk pack -H)
    # See "Code signing" in README.md. No certificate is needed to run this script.
    if ($SignParams) {
        if (-not $IsWindows) {
            throw 'Code signing with -SignParams needs Windows (signtool).'
        }
        $packArguments += @('--signParams', $SignParams)
    }
    # -------------------------------------------------------------------------------------------------

    # vpk is a local tool (.config/dotnet-tools.json): "dotnet tool run vpk" uses the pinned version.
    Write-Command ('dotnet tool run vpk ' + ($packArguments -join ' '))
    dotnet tool run vpk @packArguments
    Assert-Success 'vpk pack'

    Write-Step "Release files in $releaseDir"
    foreach ($file in Get-ChildItem -LiteralPath $releaseDir -File | Sort-Object Name) {
        Write-Host ('    {0,-48} {1,12:N0} KB' -f $file.Name, [math]::Ceiling($file.Length / 1KB))
    }
    Write-Host "Done. Next step: ./build/publish.ps1 -Channel $Channel -Destination <folder>" -ForegroundColor Green
}
finally {
    Pop-Location
}
