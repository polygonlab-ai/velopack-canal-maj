#Requires -Version 7.2
<#
.SYNOPSIS
    Copies the release files of one channel to the update server, packages first and release index last.

.DESCRIPTION
    Source: artifacts/releases/<channel>/, as produced by build/pack.ps1.

    Order of the copy, so that an installed application never reads an index that points to a missing file:
      1. packages (*.nupkg), full then delta;
      2. installer and portable archive (<id>-<channel>-Setup.exe, <id>-<channel>-Portable.zip);
      3. index files: assets.<channel>.json, RELEASES-<channel>, then releases.<channel>.json, the file the
         application reads first, always last.

    Before copying anything, the script checks that every package listed in releases.<channel>.json is present
    in the source folder. Every file name carries the channel, so publishing one channel never changes the files
    of another channel in the same server folder.

    Two ways to reach the server:
      -Destination <folder>          local folder, mapped drive or network share (\\server\share\sampleapp).
                                     Unchanged packages are skipped; each index file is written under a
                                     temporary name, then renamed, so it is replaced in one step.
      -UploadCommand { param($File) ... }
                                     your own command, run once per file in the order above
                                     (scp, rsync, aws s3 cp...). It must fail with a non-zero exit code.

    -WhatIf lists what would be copied, and copies nothing.

.EXAMPLE
    ./build/publish.ps1 -Channel stable -Destination \\fileserver\updates\sampleapp -WhatIf

.EXAMPLE
    ./build/publish.ps1 -Channel beta -UploadCommand { param($File) scp $File updates-server:/srv/updates/sampleapp/ }
#>
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'Copy')]
param(
    # Channel to publish (its folder under -SourceRoot).
    [ValidatePattern('^[a-z][a-z0-9-]{0,31}$')]
    [string] $Channel = 'stable',

    # Folder served by the web server for this application.
    [Parameter(Mandatory, ParameterSetName = 'Copy')]
    [string] $Destination,

    # Command run for each file; receives the full path of the file as its only argument.
    [Parameter(Mandatory, ParameterSetName = 'Command')]
    [scriptblock] $UploadCommand,

    # Root of the release folders produced by build/pack.ps1.
    [string] $SourceRoot = 'artifacts/releases'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = if ([System.IO.Path]::IsPathRooted($SourceRoot)) { Join-Path $SourceRoot $Channel } else { Join-Path $root $SourceRoot $Channel }
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "No release folder for the '$Channel' channel: $source. Run build/pack.ps1 first."
}

# ---- 1. Read and check the release index ---------------------------------------------------------------
$indexName = "releases.$Channel.json"
$indexPath = Join-Path $source $indexName
if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    throw "Release index $indexName not found in $source. Run build/pack.ps1 first."
}
$index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
$listed = @($index.Assets | ForEach-Object { $_.FileName })
if ($listed.Count -eq 0) {
    throw "$indexName lists no package."
}
$missing = @($listed | Where-Object { -not (Test-Path -LiteralPath (Join-Path $source $_) -PathType Leaf) })
if ($missing.Count -gt 0) {
    throw "$indexName lists packages that are not in $source (nothing was published): $($missing -join ', ')"
}

# ---- 2. Order the files: packages, installers, then index files (releases.<channel>.json last) ----------
$files = @(Get-ChildItem -LiteralPath $source -File)
$foreign = @($files | Where-Object { $_.Name -notmatch "(^|[-.])$([regex]::Escape($Channel))([-.]|$)" })
if ($foreign.Count -gt 0) {
    throw "Files without the '$Channel' channel in their name found in $source (nothing was published): $($foreign.Name -join ', ')"
}

function Get-Rank([System.IO.FileInfo] $File) {
    switch -Regex ($File.Name) {
        '-full\.nupkg$' { return 0 }
        '-delta\.nupkg$' { return 1 }
        '\.nupkg$' { return 2 }
        '^assets\..+\.json$' { return 4 }
        '^RELEASES-' { return 5 }
        '^releases\..+\.json$' { return 6 }
        default { return 3 }   # Setup.exe, Portable.zip, .msi
    }
}
$ordered = @($files | Sort-Object @{ Expression = { Get-Rank $_ } }, Name)
$isIndex = { param($File) (Get-Rank $File) -ge 4 }

Write-Host "Publishing the '$Channel' channel: $($ordered.Count) files from $source"
Write-Host ("  index checked: {0} package(s) listed, all present" -f $listed.Count)

# ---- 3. Copy ---------------------------------------------------------------------------------------------
function Test-SameFile([string] $Left, [string] $Right) {
    if (-not (Test-Path -LiteralPath $Right -PathType Leaf)) { return $false }
    if ((Get-Item -LiteralPath $Left).Length -ne (Get-Item -LiteralPath $Right).Length) { return $false }
    return (Get-FileHash -LiteralPath $Left -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $Right -Algorithm SHA256).Hash
}

if ($PSCmdlet.ParameterSetName -eq 'Copy' -and -not $WhatIfPreference) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
}

$copied = 0
$skipped = 0
foreach ($file in $ordered) {
    $target = if ($PSCmdlet.ParameterSetName -eq 'Copy') { Join-Path $Destination $file.Name } else { 'upload command' }
    if ($PSCmdlet.ParameterSetName -eq 'Copy' -and -not (& $isIndex $file) -and (Test-SameFile $file.FullName $target)) {
        Write-Host "  unchanged  $($file.Name)"
        $skipped++
        continue
    }
    if (-not $PSCmdlet.ShouldProcess($target, "Publish $($file.Name)")) {
        continue
    }

    if ($PSCmdlet.ParameterSetName -eq 'Copy') {
        if (& $isIndex $file) {
            # Write under a temporary name, then rename: readers see the old index or the new one, never half a file.
            $temporary = Join-Path $Destination ('.' + $file.Name + '.tmp')
            Copy-Item -LiteralPath $file.FullName -Destination $temporary -Force
            Move-Item -LiteralPath $temporary -Destination $target -Force
        }
        else {
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }
    else {
        $global:LASTEXITCODE = 0
        & $UploadCommand $file.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "Upload command failed for $($file.Name) (exit code $LASTEXITCODE). Files after it were not published; the index was not updated unless listed above."
        }
    }
    Write-Host "  published  $($file.Name)"
    $copied++
}

if ($WhatIfPreference) {
    Write-Host 'WhatIf: nothing was copied.' -ForegroundColor Yellow
}
else {
    Write-Host ("Done: {0} file(s) published, {1} unchanged. Index: {2}" -f $copied, $skipped, $indexName) -ForegroundColor Green
}
