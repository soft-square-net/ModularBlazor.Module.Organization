<#
.SYNOPSIS
  Packs the Organization bundle: artifacts/bundle/organization-<version>.zip.

.DESCRIPTION
  One release of the module is one bundle, in the layout of the platform's planned .modpkg (whose packer and one-step
  install are not built yet, so an admin installs the parts one by one, in this order):

    module.json                                   id, name, version and the parts below, in install order
    server/organization-<version>.zip             the server part: the contents of WebApi.Modules.Organization.Web's
                                                  build output; upload on /host-modules (Host Modules) and activate
    client/soft.square.net.WebApi.Modules.Organization.Client.dll   the hidden API client; /client-modules, first
    client/soft.square.net.Module.Organization.dll                  the UI; /client-modules, after the API client
    contracts/soft.square.net.Organization.Contracts.<version>.nupkg  for other modules (also on the package feed)
    contracts/soft.square.net.Organization.Client.<version>.nupkg     for callers outside the client module catalog

  The version is the projects' (Directory.Build.props: VersionPrefix-VersionSuffix) unless -Version is given; the
  server manifest, the client parts and the packages all get it. It builds and packs only: nothing is published.
  Windows PowerShell 5.1 or PowerShell 7.

.EXAMPLE
  ./scripts/Pack-Bundle.ps1                          # organization-1.0.0-preview.1.zip (the version in the repo)
  ./scripts/Pack-Bundle.ps1 -Version 1.0.0-ci.42     # what CI does on a pull request
#>
param(
    [string] $Version,
    [string] $Output = (Join-Path $PSScriptRoot '..\artifacts\bundle'),
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$web = Join-Path $root 'src\WebApi.Modules.Organization.Web'
$apiClient = Join-Path $root 'src\WebApi.Modules.Organization.Client'
$ui = Join-Path $root 'src\Module.Organization'
$contracts = Join-Path $root 'src\Organization.Contracts'

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed ($LASTEXITCODE)." }
}

# A zip of a folder's contents with '/' separators (Windows PowerShell's Compress-Archive writes '\', which other
# platforms' unzip and .NET on Linux do not read as folders).
function New-Zip([string] $folder, [string] $destination) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    if (Test-Path $destination) { Remove-Item $destination -Force }
    $base = (Resolve-Path $folder).Path.TrimEnd('\', '/')
    $zip = [System.IO.Compression.ZipFile]::Open($destination, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $base -Recurse -File) {
            $entry = $file.FullName.Substring($base.Length + 1).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $zip.Dispose() }
}

# EF design assets copy the host's assemblies into the output (README, "Building"); a bundle never ships them.
Remove-Item Env:EfDesignAssets -ErrorAction SilentlyContinue

if (-not $Version) {
    $Version = (& dotnet msbuild (Join-Path $web 'WebApi.Modules.Organization.Web.csproj') -nologo -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $Version) { throw 'Could not read the projects'' version.' }
}
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "'$Version' is not a SemVer version (1.2.3 or 1.2.3-preview.1)." }
$name = "organization-$Version"
Write-Host "Packing $name"

$versionArgs = @("-p:Version=$Version", '-c', $Configuration, '--nologo')
Invoke-Dotnet build (Join-Path $web 'WebApi.Modules.Organization.Web.csproj') @versionArgs
Invoke-Dotnet build (Join-Path $ui 'Module.Organization.csproj') @versionArgs   # builds the API client too
$staging = Join-Path ([IO.Path]::GetTempPath()) ("$name-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $staging 'server'), (Join-Path $staging 'client'), (Join-Path $staging 'contracts') | Out-Null
try {
    Invoke-Dotnet pack (Join-Path $contracts 'Organization.Contracts.csproj') @versionArgs --no-build -o (Join-Path $staging 'contracts')
    Invoke-Dotnet pack (Join-Path $apiClient 'WebApi.Modules.Organization.Client.csproj') @versionArgs --no-build -o (Join-Path $staging 'contracts')

    # The server part: the contents of the entry project's output folder. The host provides the platform's assemblies;
    # a copy of one in the zip means the build ran with EF design assets.
    $serverOut = Join-Path $web "bin\$Configuration\net10.0"
    $hostShared = Get-ChildItem $serverOut -Filter '*.dll' | Where-Object {
        $_.Name -match '^(soft\.square\.net\.WebApi\.(Contracts|Data|SharedKernel)|Microsoft\.EntityFrameworkCore|Carter|Npgsql)'
    }
    if ($hostShared) { throw "The server output holds host assemblies ($($hostShared.Name -join ', ')): rebuild without EfDesignAssets." }
    $serverZip = "server/$name.zip"
    New-Zip $serverOut (Join-Path $staging $serverZip)

    $clientParts = @(
        (Join-Path $apiClient "bin\$Configuration\net10.0\soft.square.net.WebApi.Modules.Organization.Client.dll"),
        (Join-Path $ui "bin\$Configuration\net10.0\soft.square.net.Module.Organization.dll"))
    foreach ($part in $clientParts) { Copy-Item $part (Join-Path $staging 'client') }

    $manifest = [ordered]@{
        id = 'organization'
        name = 'Organization'
        version = $Version
        server = $serverZip
        client = @($clientParts | ForEach-Object { 'client/' + (Split-Path $_ -Leaf) })
        contracts = @(Get-ChildItem (Join-Path $staging 'contracts') -Filter '*.nupkg' | Sort-Object Name | ForEach-Object { 'contracts/' + $_.Name })
        install = 'Upload server on /host-modules and activate it (Organization:Mode and the connector settings: docs/en/settings.md), then the client parts on /client-modules in the order listed.'
    }
    [IO.File]::WriteAllText((Join-Path $staging 'module.json'), ($manifest | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))

    New-Item -ItemType Directory -Force $Output | Out-Null
    $bundle = Join-Path (Resolve-Path $Output).Path "$name.zip"
    New-Zip $staging $bundle
    Write-Host "Bundle: $bundle"
    Get-ChildItem $staging -Recurse -File | ForEach-Object { '  ' + $_.FullName.Substring($staging.Length + 1).Replace('\', '/') }
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
