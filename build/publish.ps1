<#
.SYNOPSIS
Publishes the release folder and zip (M6.6): sfp2onenote.exe (the command line) and sfp2onenote-app.exe (the window)
as self-contained single-file win-x64 executables in ONE folder, with the templates folder and LICENSE.md beside them,
then checks the folder and zips it as sfp2onenote-<version>-win-x64.zip next to it.

.DESCRIPTION
The app starts the sfp2onenote.exe beside itself for the daily scheduled task, so the two always ship together.
Trimming stays off: WPF is not trim-safe. Runs under Windows PowerShell 5.1 and PowerShell 7 on any OS with the
.NET 8 SDK (on Linux and macOS the WPF app is cross-published through EnableWindowsTargeting). The release workflow
(.github/workflows/release.yml) runs it on windows-latest for every v* tag.

.PARAMETER Out
The release folder. Default: artifacts\sfp2onenote-win-x64 under the repository root, emptied on every run. Any other
folder must be empty or missing (the script never deletes it).

.PARAMETER Version
Stamped into both executables and the zip name, for example 1.2.0; a leading "v" is dropped so a tag name works.
Default: the Version property of Directory.Build.props, read through MSBuild.

.EXAMPLE
pwsh -File build/publish.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File build\publish.ps1 -Version 1.2.0 -Out D:\release\sfp2onenote
#>
[CmdletBinding()]
param(
    [string] $Out = '',
    [string] $Version = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Dotnet {
    param([Parameter(Mandatory = $true)] [string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

function Format-Megabytes {
    param([Parameter(Mandatory = $true)] [string] $Path)
    $megabytes = (Get-Item -LiteralPath $Path).Length / 1MB
    return $megabytes.ToString('0.0', [System.Globalization.CultureInfo]::InvariantCulture) + ' MB'
}

$root = Split-Path -Parent $PSScriptRoot
$cliProject = [System.IO.Path]::Combine($root, 'src', 'Sfp.Cli', 'Sfp.Cli.csproj')
$appProject = [System.IO.Path]::Combine($root, 'src', 'Sfp.App', 'Sfp.App.csproj')
$templatesRoot = [System.IO.Path]::Combine($root, 'templates')
$defaultOut = [System.IO.Path]::Combine($root, 'artifacts', 'sfp2onenote-win-x64')

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 8 SDK (dotnet) is not on PATH.'
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    # The last non-empty line: the SDK's first-run welcome text, if any, comes before the value.
    $lines = @(& dotnet msbuild $cliProject '-getProperty:Version')
    $Version = $lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0 -or $null -eq $Version) {
        throw 'Could not read the Version property through MSBuild.'
    }
    $Version = $Version.ToString()
}
$Version = $Version.Trim() -replace '^v', ''
if ($Version -notmatch '^[0-9]+(\.[0-9]+){1,3}(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "Version '$Version' is not a version number such as 1.2.0 or 1.2.0-rc.1."
}

if ([string]::IsNullOrWhiteSpace($Out)) {
    $outDir = $defaultOut
    if (Test-Path -LiteralPath $outDir) {
        Remove-Item -LiteralPath $outDir -Recurse -Force
    }
}
else {
    $outDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
}
$outDir = [System.IO.Path]::GetFullPath($outDir).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
[System.IO.Directory]::CreateDirectory($outDir) | Out-Null
$rootFull = [System.IO.Path]::GetFullPath($root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
if ($outDir -eq $rootFull -or $null -ne (Get-ChildItem -LiteralPath $outDir -Force | Select-Object -First 1)) {
    throw "The release folder $outDir is not empty; empty it or choose another -Out."
}
$zip = [System.IO.Path]::Combine((Split-Path -Parent $outDir), "sfp2onenote-$Version-win-x64.zip")

Write-Host "Publishing sfp2onenote $Version for win-x64 into $outDir"
$publish = @('-c', 'Release', '-r', 'win-x64', '--self-contained',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    "-p:Version=$Version", '-o', $outDir)
Invoke-Dotnet (@('publish', $cliProject) + $publish)
Invoke-Dotnet (@('publish', $appProject) + $publish)
Copy-Item -LiteralPath ([System.IO.Path]::Combine($root, 'LICENSE.md')) -Destination ([System.IO.Path]::Combine($outDir, 'LICENSE.md'))

# Check the folder: both executables are single-file bundles (a bare app host is about 150 KB, a self-contained
# bundle about 100 MB) and templates\ is a byte-identical copy of the shipped templates folder.
$problems = New-Object System.Collections.Generic.List[string]
foreach ($exe in @('sfp2onenote.exe', 'sfp2onenote-app.exe')) {
    $path = [System.IO.Path]::Combine($outDir, $exe)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $problems.Add("$exe is missing")
    }
    elseif ((Get-Item -LiteralPath $path).Length -lt 10MB) {
        $problems.Add("$exe is $(Format-Megabytes $path), not a self-contained single-file bundle")
    }
}
foreach ($required in @('sfp-quarterly.json', 'holidays-us.json')) {
    if (-not (Test-Path -LiteralPath ([System.IO.Path]::Combine($outDir, 'templates', $required)) -PathType Leaf)) {
        $problems.Add("templates/$required is missing")
    }
}
$fontsDir = [System.IO.Path]::Combine($outDir, 'templates', 'fonts')
if (-not (Test-Path -LiteralPath $fontsDir) -or @(Get-ChildItem -LiteralPath $fontsDir -Filter '*.otf' -File).Count -eq 0) {
    $problems.Add('templates/fonts/*.otf is missing')
}
foreach ($source in Get-ChildItem -LiteralPath $templatesRoot -Recurse -File) {
    $relative = $source.FullName.Substring($templatesRoot.Length).TrimStart('\', '/') -replace '\\', '/'
    $copy = [System.IO.Path]::Combine($outDir, 'templates', $relative)
    if (-not (Test-Path -LiteralPath $copy -PathType Leaf)) {
        $problems.Add("templates/$relative is missing")
    }
    elseif ((Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash) {
        $problems.Add("templates/$relative differs from the repository copy")
    }
}
if ($problems.Count -gt 0) {
    $problems | Select-Object -Unique | ForEach-Object { Write-Host "publish.ps1: $_" }
    throw "The release folder $outDir is incomplete."
}
$expected = @('sfp2onenote.exe', 'sfp2onenote-app.exe', 'templates', 'LICENSE.md')
foreach ($entry in Get-ChildItem -LiteralPath $outDir -Force) {
    if ($expected -notcontains $entry.Name) {
        Write-Warning "Unexpected entry in the release folder: $($entry.Name)"
    }
}

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($outDir, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$templateCount = @(Get-ChildItem -LiteralPath ([System.IO.Path]::Combine($outDir, 'templates')) -Recurse -File).Count
Write-Host ''
Write-Host "Release folder: $outDir"
Write-Host "  sfp2onenote.exe      $(Format-Megabytes ([System.IO.Path]::Combine($outDir, 'sfp2onenote.exe')))"
Write-Host "  sfp2onenote-app.exe  $(Format-Megabytes ([System.IO.Path]::Combine($outDir, 'sfp2onenote-app.exe')))"
Write-Host "  templates/           $templateCount files"
Write-Host "Zip: $zip ($(Format-Megabytes $zip))"
