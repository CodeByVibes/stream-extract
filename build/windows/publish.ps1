<#
.SYNOPSIS
    Builds the StreamExtract Avalonia desktop app for Windows.

.DESCRIPTION
    Publishes StreamExtract.Desktop for a Windows runtime identifier, verifies
    the bundled native tools against the SHA-256 hashes recorded in
    tools-manifest.json, and stages tools-manifest.json, tools/ and licenses/
    next to the executable.

    StreamExtract.Desktop.csproj does not copy the native tool bundle itself
    (unlike the WinForms project), and the app validates those tools at
    startup. A bundle that skips the staging step still launches, but every
    extraction fails, so this script refuses to finish without them.

    Two layouts are supported:

      folder      One self-contained folder holding the executable and every
                  assembly and native library it needs.
      single file (--SingleFile) One StreamExtract.Desktop.exe with the runtime,
                  the managed assemblies and the native libraries bundled
                  inside it. tools/, licenses/ and tools-manifest.json stay
                  loose next to it, because the app runs the tools from disk
                  and validates them at startup. Symbols are removed.

    The script runs on Windows with PowerShell 7 (pwsh) and on Linux/macOS with
    pwsh for cross-publishing. It never writes outside the output directory and
    the dist directory.

.PARAMETER Configuration
    Build configuration to publish. Defaults to Release.

.PARAMETER RuntimeIdentifier
    Windows runtime identifier to publish for. Defaults to win-x64. The
    identifier must have native tools recorded in tools-manifest.json.

.PARAMETER OutputDirectory
    Staging directory for the app bundle. Defaults to
    dist/publish-<RuntimeIdentifier>[-singlefile]/StreamExtract. When supplied,
    the directory is not cleaned first.

.PARAMETER FrameworkDependent
    Publish a framework-dependent bundle instead of a self-contained one. The
    target machine then needs the .NET 10 runtime installed.

.PARAMETER SingleFile
    Bundle the runtime and all assemblies into a single executable.

.PARAMETER SkipArchive
    Skip creating the zip archive of the staged bundle.

.EXAMPLE
    pwsh -File build/windows/publish.ps1

    Publishes a self-contained folder bundle to
    dist/publish-win-x64/StreamExtract and zips it to
    dist/StreamExtract-win-x64.zip.

.EXAMPLE
    pwsh -File build/windows/publish.ps1 -SingleFile

    Publishes a single StreamExtract.Desktop.exe with tools/ and licenses/
    next to it, and zips the staged folder to
    dist/StreamExtract-win-x64-singlefile.zip.

.EXAMPLE
    pwsh -File build/windows/publish.ps1 -Configuration Debug -SkipArchive

    Publishes a debug bundle and leaves the staging directory unarchived.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$RuntimeIdentifier = 'win-x64',

    [string]$OutputDirectory,

    [switch]$FrameworkDependent,

    [switch]$SingleFile,

    [switch]$SkipArchive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Get-StagingDirectory {
    return [System.IO.Path]::GetFullPath($OutputDirectory)
}

$rootDir = (Resolve-Path -LiteralPath (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
$distDir = Join-Path $rootDir 'dist'
$explicitOutput = $PSBoundParameters.ContainsKey('OutputDirectory')
$flavorSuffix = if ($SingleFile) { '-singlefile' } else { '' }

if (-not $explicitOutput) {
    $OutputDirectory = Join-Path (Join-Path $distDir "publish-$RuntimeIdentifier$flavorSuffix") 'StreamExtract'
}
$stagingDir = Get-StagingDirectory
$archivePath = Join-Path $distDir "StreamExtract-$RuntimeIdentifier$flavorSuffix.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK ('dotnet') was not found on PATH."
}

# --- Verify the committed native tools before anything is published ----------

$manifestPath = Join-Path $rootDir 'tools-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Missing native tool manifest: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

Write-Step "Verifying native tools for $RuntimeIdentifier against tools-manifest.json"
$verifiedTools = @()
foreach ($tool in $manifest.tools) {
    if ($tool.rid -ne $RuntimeIdentifier) { continue }

    $source = Join-Path (Join-Path $rootDir 'tools') $tool.filename
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Native tool is missing: $source"
    }

    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $tool.sha256.ToLowerInvariant()) {
        throw "Checksum mismatch for $($tool.filename): expected $($tool.sha256), got $hash. Restore tools/ from git instead of replacing the binaries."
    }

    $verifiedTools += [pscustomobject]@{ Id = $tool.id; Filename = $tool.filename; Source = $source }
    Write-Host "    $($tool.filename)  $hash"
}

$requiredIds = @('MkvMerge', 'MkvExtract', 'Mp4Box')
$verifiedIds = @($verifiedTools | ForEach-Object { $_.Id })
$missingIds = @($requiredIds | Where-Object { $_ -notin $verifiedIds })
if ($missingIds.Count -gt 0) {
    throw "tools-manifest.json has no verified $RuntimeIdentifier build for: $($missingIds -join ', '). Add the native tools for that runtime before publishing it."
}

# --- Publish -----------------------------------------------------------------

if (-not $explicitOutput -and (Test-Path -LiteralPath $stagingDir)) {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$project = Join-Path $rootDir 'StreamExtract.Desktop/StreamExtract.Desktop.csproj'

$publishArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $RuntimeIdentifier,
    '--self-contained', $selfContained,
    '-o', $stagingDir
)
if ($SingleFile) {
    # Fold the runtime, the managed assemblies and the native Avalonia/Skia
    # libraries into one executable. tools/, licenses/ and tools-manifest.json
    # stay loose next to it: the app executes the tools from disk and validates
    # them against the manifest at startup.
    $publishArgs += '-p:PublishSingleFile=true'
    $publishArgs += '-p:IncludeNativeLibrariesForSelfExtract=true'
    $publishArgs += '-p:EnableCompressionInSingleFile=true'
    $publishArgs += '-p:DebugType=none'
}

Write-Step "Publishing StreamExtract.Desktop ($Configuration, $RuntimeIdentifier, self-contained: $selfContained, single file: $SingleFile)"
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# --- Stage the native tool bundle next to the executable ---------------------

Write-Step "Staging the native tool bundle"
$toolsDir = Join-Path $stagingDir 'tools'
$licensesDir = Join-Path $stagingDir 'licenses'
New-Item -ItemType Directory -Force -Path $toolsDir, $licensesDir | Out-Null

Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingDir 'tools-manifest.json') -Force
foreach ($tool in $verifiedTools) {
    Copy-Item -LiteralPath $tool.Source -Destination (Join-Path $toolsDir $tool.Filename) -Force
}
Copy-Item -Path (Join-Path (Join-Path $rootDir 'licenses') '*.txt') -Destination $licensesDir -Force

$exePath = Join-Path $stagingDir 'StreamExtract.Desktop.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Publish finished without producing $exePath."
}

if ($SingleFile) {
    # Everything except tools/, licenses/ and tools-manifest.json must live
    # inside the executable; a loose assembly means the bundle is not single.
    $strayAssemblies = @(Get-ChildItem -LiteralPath $stagingDir -File -Filter '*.dll')
    if ($strayAssemblies.Count -gt 0) {
        throw "Single-file publish left loose assemblies next to the executable: $(($strayAssemblies.Name) -join ', ')."
    }
    # Native packages ship symbols that would dwarf the executable.
    Get-ChildItem -LiteralPath $stagingDir -File -Filter '*.pdb' | Remove-Item -Force
}

# --- Archive -----------------------------------------------------------------

if (-not $SkipArchive) {
    Write-Step "Creating $archivePath"
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    Compress-Archive -Path $stagingDir -DestinationPath $archivePath -CompressionLevel Optimal
}

$bundleSize = (Get-ChildItem -LiteralPath $stagingDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
$exeSize = (Get-Item -LiteralPath $exePath).Length
$mode = if ($SingleFile) { 'single file' } else { 'folder' }

Write-Host ''
Write-Host 'Bundle ready.' -ForegroundColor Green
Write-Host "  Directory  : $stagingDir"
Write-Host "  Executable : $exePath ($([math]::Round($exeSize / 1MB, 1)) MB)"
Write-Host "  Mode       : $mode"
if (-not $SkipArchive) {
    $archiveSize = (Get-Item -LiteralPath $archivePath).Length
    Write-Host "  Archive    : $archivePath ($([math]::Round($archiveSize / 1MB, 1)) MB)"
}
Write-Host "  Size       : $([math]::Round($bundleSize / 1MB, 1)) MB"
Write-Host "  Tools      : $(($verifiedTools.Filename) -join ', ')"
