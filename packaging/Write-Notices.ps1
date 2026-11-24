<#
.SYNOPSIS
Write THIRD-PARTY-NOTICES.txt for a published Prism build: every NuGet package that ships in it,
the .NET runtime, and the native libraries staged by native/build.ps1, with their full license texts.
.EXAMPLE
./packaging/Write-Notices.ps1 -PublishDirectory ./artifacts/installers/win-x64/publish -AssetsFile ./artifacts/installers/build/obj/Prism.Desktop/project.assets.json
.EXAMPLE
./packaging/Write-Notices.ps1 -Web -PublishDirectory ./artifacts/browser/wwwroot -AssetsFile ./Prism.Browser/obj/project.assets.json -RuntimePack <dotnet>/packs/Microsoft.NETCore.App.Runtime.Mono.browser-wasm/10.0.3 -Commit <sha>
.NOTES
-Web writes the notices for the browser build, where the native libraries are statically linked into
dotnet.native.wasm (LGPL-2.1 section 6a) instead of shipped as shared libraries (section 6b).
#>
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string[]]$AssetsFile,
    [string]$Runtime,
    # Browser build: static linking, Emscripten, bundled fonts; no Netgen, no Visual C++ runtime.
    [switch]$Web,
    # Runtime pack folder when it isn't restored through NuGet (the browser pack comes from the wasm-tools workload).
    [string]$RuntimePack,
    # Prism commit the build was made from; the web notices point to it for rebuilding with a modified library.
    [string]$Commit,
    [string]$Emscripten,
    [string]$Output = (Join-Path $PublishDirectory 'THIRD-PARTY-NOTICES.txt')
)
$ErrorActionPreference = 'Stop'
$repo = 'https://github.com/chrizpro/prism'
$rule = '=' * 78
$out = [System.Text.StringBuilder]::new()
function Line([string]$text = '') { [void]$out.AppendLine($text) }
function Section([string]$title) { Line; Line $rule; Line $title; Line $rule; Line }

$mit = @'
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO
EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
USE OR OTHER DEALINGS IN THE SOFTWARE.
'@

# ------------------------------------------------------------------ native libraries (vcpkg)
$licenses = Join-Path $PublishDirectory 'licenses'
$native = @()
if (Test-Path -LiteralPath $licenses) {
    foreach ($dir in Get-ChildItem -LiteralPath $licenses -Directory | Sort-Object Name) {
        $spdxFile = Join-Path $dir.FullName 'vcpkg.spdx.json'
        $version = ''; $source = ''
        if (Test-Path -LiteralPath $spdxFile) {
            $spdx = Get-Content -LiteralPath $spdxFile -Raw | ConvertFrom-Json
            $package = $spdx.packages | Where-Object { $_.name -eq $dir.Name } | Select-Object -First 1
            if ($package) { $version = $package.versionInfo; $source = $package.downloadLocation }
            $upstream = $spdx.packages | Where-Object { $_.downloadLocation -match '^git\+|^https?:' -and $_.name -ne $dir.Name } | Select-Object -First 1
            if ($upstream) { $source = $upstream.downloadLocation }
        }
        $native += [pscustomobject]@{ Name = $dir.Name; Version = $version; Source = $source; Text = Get-Content -LiteralPath (Join-Path $dir.FullName 'copyright') -Raw }
    }
}

# ------------------------------------------------------------------ NuGet packages that ship
$packages = @{}
foreach ($file in $AssetsFile) {
    $assets = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -AsHashtable
    $targetName = $assets.targets.Keys | Where-Object { $Runtime -and $_ -like "*/$Runtime" } | Select-Object -First 1
    if (!$targetName) { $targetName = $assets.targets.Keys | Select-Object -First 1 }
    $target = $assets.targets[$targetName]
    $roots = $assets.packageFolders.Keys
    foreach ($key in $target.Keys) {
        $library = $assets.libraries[$key]
        if ($library.type -ne 'package') { continue }
        $entry = $target[$key]
        # Packages with nothing to copy at runtime (analyzers, build tasks, excluded dev tools) do not ship.
        $files = @($entry.runtime, $entry.native, $entry.runtimeTargets, $entry.resource) | Where-Object { $_ } | ForEach-Object { $_.Keys } | Where-Object { $_ -notmatch '_\._$' }
        if (!$files) { continue }
        $folder = $roots | ForEach-Object { Join-Path $_ $library.path } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($folder) { $packages[$key] = $folder }
    }
}

# ------------------------------------------------------------------ write
Line 'PRISM THIRD-PARTY NOTICES'
Line
Line 'Prism includes software developed by the third parties listed below. Each component is used under'
if ($Web) {
    Line 'its own license, reproduced in full in this file. This is the web version: the native libraries are'
    Line 'compiled into dotnet.native.wasm, and their license files are also in the "licenses" folder next to'
    Line 'this file.'
} else {
    Line 'its own license, reproduced in full in this file. Native libraries are dynamically linked and their'
    Line 'license files are also in the "licenses" folder next to this file.'
}
Line
Line "Prism is released under the MIT License (LICENSE.txt). Source code: $repo"
if ($Commit) {
    $sha = ($Commit -split ' ')[0]
    Line "This build was made from commit $sha`: $repo/tree/$sha"
    if ($Commit -ne $sha) { Line 'It also contained uncommitted changes, so that commit alone may not reproduce it exactly.' }
}

if ($Web) {
Section 'LGPL library: source code and relinking'
Line 'Open CASCADE Technology is licensed under the GNU Lesser General Public License version 2.1 with an'
Line 'additional exception (both included below). The web version links it statically into'
Line 'dotnet.native.wasm, as permitted by section 6(a) of the license.'
Line
foreach ($lib in $native | Where-Object { $_.Name -eq 'opencascade' }) {
    Line ("  {0} {1}" -f $lib.Name, $lib.Version)
    if ($lib.Source) { Line "    Upstream source: $($lib.Source)" }
}
Line
Line 'Corresponding source: licenses/Prism-LGPL-sources.zip next to this file contains the upstream'
Line 'Open CASCADE archive and the vcpkg recipe, patches and triplet this build used.'
Line
Line 'To run the web version with a modified Open CASCADE: Prism is open source under the MIT License, so'
Line 'the complete source of everything linked with the library is available. Check out the commit above,'
Line 'build your Open CASCADE with the wasm32-emscripten-eh triplet in native/triplets, then run'
Line 'native/build-wasm.ps1 and packaging/Publish-Browser.ps1 (see docs/interface/browser/README.md).'
Line 'The MIT License permits modification and reverse engineering of Prism for this purpose.'
Line "If the archive is missing, request it through $repo/issues; it will be provided for at least"
Line 'three years after this build.'
} else {
Section 'LGPL libraries: source code and replacement'
Line 'Open CASCADE Technology and Netgen are licensed under the GNU Lesser General Public License version'
Line '2.1 (Open CASCADE Technology with an additional exception, included below). Prism links them as'
Line 'separate shared libraries, so you may replace them with your own builds of compatible versions.'
Line
foreach ($lib in $native | Where-Object { $_.Name -in 'opencascade', 'netgen' }) {
    Line ("  {0} {1}" -f $lib.Name, $lib.Version)
    if ($lib.Source) { Line "    Upstream source: $($lib.Source)" }
}
Line
Line 'The complete corresponding source code of these libraries, as built for this release (upstream'
Line 'archives plus the vcpkg build recipes and patches applied to them), is published with each release'
Line "as Prism-LGPL-sources.zip at $repo/releases."
Line "If it is missing, request it through $repo/issues; it will be provided for at least"
Line 'three years after this release.'

Section 'FreeType'
Line 'Portions of this software are copyright (c) The FreeType Project (www.freetype.org).'
Line 'All rights reserved.'
}

Section '.NET runtime'
# Self-contained builds carry the runtime pack restored for the target; use its own notices.
$runtimePack = $RuntimePack
foreach ($file in $AssetsFile) {
    if ($runtimePack) { break }
    $assets = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -AsHashtable
    foreach ($framework in $assets.project.frameworks.Values) {
        $pack = $framework.downloadDependencies | Where-Object { $Runtime -and $_.name -eq "Microsoft.NETCore.App.Runtime.$Runtime" } | Select-Object -First 1
        if (!$pack) { continue }
        $version = $pack.version.Trim('[', ']').Split(',')[0].Trim()
        $runtimePack = $assets.packageFolders.Keys | ForEach-Object { Join-Path $_ "microsoft.netcore.app.runtime.$($Runtime.ToLowerInvariant())/$version" } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($runtimePack) { break }
    }
    if ($runtimePack) { break }
}
if ($runtimePack) {
    Line "Runtime pack: $(Split-Path (Split-Path $runtimePack) -Leaf) $(Split-Path $runtimePack -Leaf)"
    Line
    foreach ($name in 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') {
        $path = Join-Path $runtimePack $name
        if (Test-Path -LiteralPath $path) { Line (Get-Content -LiteralPath $path -Raw); Line }
    }
} else {
    Line '.NET is licensed under the MIT license. License and third-party notices:'
    Line 'https://github.com/dotnet/runtime/blob/main/LICENSE.TXT'
    Line 'https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT'
}

$printed = @{}
foreach ($key in $packages.Keys | Sort-Object) {
    $folder = $packages[$key]
    $nuspec = Get-ChildItem -LiteralPath $folder -Filter *.nuspec | Select-Object -First 1
    [xml]$xml = Get-Content -LiteralPath $nuspec.FullName -Raw
    $meta = $xml.package.metadata
    Section ("{0}" -f ($key -replace '/', ' '))
    if ($meta.projectUrl) { Line "Project: $($meta.projectUrl)" }
    if ($meta.copyright) { Line "Copyright: $($meta.copyright)" }
    $license = $meta.license
    $texts = @()
    if ($license -and $license.type -eq 'file') { $texts += Join-Path $folder $license.'#text' }
    $texts += Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|THIRD-PARTY-NOTICES|NOTICE)' } | ForEach-Object FullName
    $texts = $texts | Select-Object -Unique | Where-Object { Test-Path -LiteralPath $_ }
    if ($license -and $license.type -eq 'expression') { Line "License: $($license.'#text')" }
    elseif ($meta.licenseUrl) { Line "License: $($meta.licenseUrl)" }
    Line
    if ($texts) {
        foreach ($t in $texts) {
            # Platform variants of one package carry identical notices; print each text once.
            $body = Get-Content -LiteralPath $t -Raw
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($body)))
            if ($printed.ContainsKey($hash)) { Line ("{0}: same text as under {1} above." -f (Split-Path $t -Leaf), $printed[$hash]) }
            else { $printed[$hash] = ($key -replace '/', ' '); Line $body }
            Line
        }
    }
    elseif ($license -and $license.'#text' -eq 'MIT') {
        $holder = ($meta.copyright ?? $meta.authors) -replace '^\s*(Copyright\s*)?(\(c\)|©)?\s*', ''
        Line "Copyright (c) $holder"; Line; Line $mit
    }
    elseif ($license) { Line "Full text: https://spdx.org/licenses/$($license.'#text').html" }
}

foreach ($lib in $native) {
    Section ("{0} {1} (native library)" -f $lib.Name, $lib.Version)
    if ($lib.Source) { Line "Source: $($lib.Source)"; Line }
    Line $lib.Text
}

if ($Web) {
    Section 'Emscripten system libraries'
    Line 'The web version is compiled with Emscripten, which links parts of its C/C++ runtime (musl libc,'
    Line 'libc++, libc++abi, compiler-rt) into dotnet.native.wasm.'
    Line
    if ($Emscripten -and (Test-Path -LiteralPath (Join-Path $Emscripten 'LICENSE'))) { Line (Get-Content -LiteralPath (Join-Path $Emscripten 'LICENSE') -Raw) }
    else { Line 'License: https://github.com/emscripten-core/emscripten/blob/main/LICENSE' }

    foreach ($font in Get-ChildItem -LiteralPath $licenses -File -Filter '*-font-license.txt' -ErrorAction SilentlyContinue | Sort-Object Name) {
        Section ("{0} (bundled fallback font)" -f ($font.BaseName -replace '-font-license$', ''))
        Line (Get-Content -LiteralPath $font.FullName -Raw)
    }
} else {
    Section 'Microsoft Visual C++ runtime (Windows only)'
    Line 'The Windows installer includes Microsoft Visual C++ runtime libraries, distributed under the terms'
    Line 'of the Microsoft Visual Studio license for distributable code.'
}

# The Inter typeface ships inside Avalonia.Fonts.Inter, whose package licence (MIT) covers Avalonia's code only; the font itself is
# under the SIL Open Font License, which asks for its notice and licence to travel with it (desktop and web, found 2026-11-23).
$interLicense = Join-Path $PSScriptRoot 'Inter-OFL.txt'
if ($packages.Keys | Where-Object { $_ -match '^Avalonia\.Fonts\.Inter' }) {
    Section 'Inter typeface (in Avalonia.Fonts.Inter)'
    Line (Get-Content -LiteralPath $interLicense -Raw)
}

Set-Content -LiteralPath $Output -Value $out.ToString() -Encoding utf8NoBOM
Write-Output ("Wrote {0}: {1} packages, {2} native libraries" -f $Output, $packages.Count, $native.Count)
