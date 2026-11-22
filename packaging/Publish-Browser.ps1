<#
.SYNOPSIS
Publish the web version of Prism (Prism.Browser) with its license notices and LGPL source archive.
.DESCRIPTION
Run native/build-wasm.ps1 first. The output folder's wwwroot is the complete site to host:
  wwwroot/THIRD-PARTY-NOTICES.txt          every shipped component, with full license texts
  wwwroot/licenses/<library>/              vcpkg copyright and SPDX files of the linked native libraries
  wwwroot/licenses/Prism-LGPL-sources.zip  Open CASCADE source, vcpkg recipe, patches and triplet
Open CASCADE is statically linked into dotnet.native.wasm, so the archive must be served with every
build that is hosted (LGPL-2.1 section 6a). See docs/interface/browser/README.md#licensing.
.EXAMPLE
./packaging/Publish-Browser.ps1
#>
[CmdletBinding()]
param(
    [string]$Output = (Join-Path $PSScriptRoot '../artifacts/browser-publish'),
    [string]$Vcpkg = 'C:/vcpkg',
    [string]$Triplet = 'wasm32-emscripten-eh',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'Prism.Browser'
$prefix = Join-Path $Vcpkg "installed/$Triplet"
if (!(Test-Path (Join-Path $root 'native/build-wasm/prism_kernel.a'))) { throw 'Run native/build-wasm.ps1 first.' }

# Stale hashed files from earlier builds break the runtime ("mono runtime and class libraries are out of sync").
foreach ($dir in (Join-Path $project 'obj'), (Join-Path $project 'bin'), $Output) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
}
& dotnet publish $project -c $Configuration -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$site = Join-Path $Output 'wwwroot'
$licenses = Join-Path $site 'licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null

# Native libraries actually linked into dotnet.native.wasm: Open CASCADE (kernel) and Eigen (FE core).
foreach ($name in 'opencascade', 'eigen3') {
    $share = Join-Path $prefix "share/$name"
    $target = Join-Path $licenses $name
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item -LiteralPath (Join-Path $share 'copyright') -Destination $target
    $spdx = Join-Path $share 'vcpkg.spdx.json'
    if (Test-Path -LiteralPath $spdx) { Copy-Item -LiteralPath $spdx -Destination $target }
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $site 'LICENSE.txt')
$fonts = Join-Path $project 'Assets/Fonts'
Copy-Item -LiteralPath (Join-Path $fonts 'Noto-OFL.txt') -Destination (Join-Path $licenses 'Noto Sans Symbols 2 and Noto Emoji-font-license.txt')
Copy-Item -LiteralPath (Join-Path $fonts 'DejaVu-LICENSE.txt') -Destination (Join-Path $licenses 'DejaVu Sans-font-license.txt')

# Corresponding source of the LGPL library, exactly as this build used it.
$commit = (& git -C $root rev-parse HEAD).Trim()
if (& git -C $root status --porcelain --untracked-files=no) { $commit += ' (with uncommitted changes)' }
$vcpkgCommit = (& git -C $Vcpkg rev-parse HEAD).Trim()
$staging = Join-Path ([IO.Path]::GetTempPath()) "prism-web-lgpl-$PID"
New-Item -ItemType Directory -Force "$staging/archives", "$staging/vcpkg-ports", "$staging/triplets" | Out-Null
try {
    $archives = Get-ChildItem (Join-Path $Vcpkg 'downloads') -File | Where-Object { $_.Name -match '(?i)occt|opencascade' }
    if (!$archives) { throw "No Open CASCADE source archive in $Vcpkg/downloads." }
    $archives | Copy-Item -Destination "$staging/archives"
    Copy-Item -Recurse (Join-Path $Vcpkg 'ports/opencascade') "$staging/vcpkg-ports/opencascade"
    Copy-Item (Join-Path $root "native/triplets/$Triplet.cmake") "$staging/triplets"
    @(
        'Source code of the LGPL-2.1 library linked into the web version of Prism.',
        "Prism commit: $commit",
        'Prism source: https://github.com/chrizpro/prism',
        '',
        'archives/      upstream Open CASCADE source archive, as downloaded by vcpkg',
        'vcpkg-ports/   the vcpkg build recipe and patches applied to it',
        'triplets/      the Emscripten triplet it was built with',
        '',
        "vcpkg commit: $vcpkgCommit (https://github.com/microsoft/vcpkg)",
        "Build: vcpkg install opencascade:$Triplet --overlay-ports=vcpkg-ports --overlay-triplets=triplets",
        'Then, in the Prism checkout: native/build-wasm.ps1 and packaging/Publish-Browser.ps1.'
    ) | Set-Content "$staging/README.txt"
    Compress-Archive -Path "$staging/*" -DestinationPath (Join-Path $licenses 'Prism-LGPL-sources.zip') -Force
}
finally { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }

$dotnetRoot = Split-Path (Get-Command dotnet).Source
$runtimePack = Get-ChildItem (Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Runtime.Mono.browser-wasm') -Directory |
    Sort-Object { [version]($_.Name -replace '-.*$', '') } -Descending | Select-Object -First 1
$emscripten = Get-ChildItem (Join-Path $dotnetRoot 'packs') -Directory -Filter 'Microsoft.NET.Runtime.Emscripten.*.Sdk.*' |
    Sort-Object Name -Descending | Select-Object -First 1 | ForEach-Object { Get-ChildItem $_.FullName -Directory | Select-Object -First 1 } |
    ForEach-Object { Join-Path $_.FullName 'tools/emscripten' }
& (Join-Path $PSScriptRoot 'Write-Notices.ps1') -Web -PublishDirectory $site -AssetsFile (Join-Path $project 'obj/project.assets.json') `
    -Runtime 'browser-wasm' -RuntimePack $runtimePack.FullName -Commit $commit -Emscripten $emscripten
Write-Output "Site ready: $site"
