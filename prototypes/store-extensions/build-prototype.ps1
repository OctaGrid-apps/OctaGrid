<#
.SYNOPSIS
    Builds and signs the two prototype MSIX packages for ADR 0001 (Store extension loading).

.DESCRIPTION
    Produces, under artifacts/prototype:
      OctaGrid.Prototype.Host.msix              host with the octagrid.connector extension point + 'octagrid-probe' alias
      OctaGrid.Prototype.SampleConnector.msix   third-party style extension package
    The packages are signed with the existing OctaGrid developer certificate from Cert:\CurrentUser\My
    (created by build-msix.ps1). Nothing is installed by this script.
#>
[CmdletBinding()]
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $root '..\..')
$out = Join-Path $repoRoot 'artifacts\prototype'
$stage = Join-Path $out 'stage'
$assets = Join-Path $repoRoot 'src\OctaGrid.Client\OctaGrid.Client.Desktop\Assets'

# --- tools -------------------------------------------------------------------------------------
$makeappx = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools') -Filter makeappx.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe not found. Run "dotnet restore OctaGrid.slnx" first (Microsoft.Windows.SDK.BuildTools).' }
$signtool = Join-Path $makeappx.DirectoryName 'signtool.exe'

# --- certificate (must already exist; not created here) ----------------------------------------
$publisher = 'CN=8D9CE064-901D-44B7-85FE-9AF30A86A5AE'
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher } | Select-Object -First 1
if (-not $cert) { throw "No signing certificate '$publisher' in Cert:\CurrentUser\My. Run .\build-msix.ps1 once to create it." }

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Publish-Package([string]$project, [string]$name) {
    $dir = Join-Path $stage $name
    Write-Host "==> Publishing $project" -ForegroundColor Cyan
    dotnet publish (Join-Path $root "$project\$project.csproj") -c $Configuration -r win-x64 --self-contained false -o $dir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $project" }

    # Plain (non-scaled) logos so no resources.pri is needed
    $logos = Join-Path $dir 'Assets'
    New-Item -ItemType Directory -Force -Path $logos | Out-Null
    foreach ($logo in 'StoreLogo', 'Square44x44Logo', 'Square150x150Logo') {
        Copy-Item (Join-Path $assets "$logo.scale-100.png") (Join-Path $logos "$logo.png")
    }
    Copy-Item (Join-Path $root "$project\AppxManifest.xml") (Join-Path $dir 'AppxManifest.xml')
    return $dir
}

function Pack-And-Sign([string]$dir, [string]$msix) {
    & $makeappx.FullName pack /d $dir /p $msix /o | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed for $msix" }
    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $msix | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for $msix" }
}

$hostDir = Publish-Package 'Probe.Host' 'host'
$connDir = Publish-Package 'Sample.Connector' 'connector'

# Variant B: the public folder carries its own copy of the connector assembly
$public = Join-Path $connDir 'Public'
New-Item -ItemType Directory -Force -Path $public | Out-Null
Copy-Item (Join-Path $root 'Sample.Connector\Public\readme.txt') $public
foreach ($f in 'Sample.Connector.dll', 'Sample.Connector.deps.json') { Copy-Item (Join-Path $connDir $f) $public }

if (Test-Path (Join-Path $connDir 'Prototype.Abstractions.dll')) {
    Write-Warning 'Prototype.Abstractions.dll was copied into the extension package; the host should supply it.'
}

Pack-And-Sign $hostDir (Join-Path $out 'OctaGrid.Prototype.Host.msix')
Pack-And-Sign $connDir (Join-Path $out 'OctaGrid.Prototype.SampleConnector.msix')

Write-Host ''
Write-Host "Built: $out" -ForegroundColor Green
Write-Host 'Next (see prototypes/store-extensions/README.md):' -ForegroundColor Green
Write-Host '  1. Trust the certificate (elevated PowerShell, once): .\artifacts\OctaGrid-Dev.cer -> LocalMachine\TrustedPeople'
Write-Host '  2. Add-AppxPackage artifacts\prototype\OctaGrid.Prototype.Host.msix'
Write-Host '  3. Add-AppxPackage artifacts\prototype\OctaGrid.Prototype.SampleConnector.msix'
Write-Host '  4. octagrid-probe'
