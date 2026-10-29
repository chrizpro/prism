[CmdletBinding()]
param(
    [ValidateSet('win-x64','linux-x64','osx-x64','osx-arm64')][string]$Runtime = 'win-x64',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.0',
    [string]$OutputDirectory,
    [string]$NativeDirectory,
    [string]$PublishDirectory,
    [string]$Compiler,
    [switch]$SkipPublish,
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/installers' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (!$PublishDirectory) { $PublishDirectory = Join-Path $OutputDirectory "$Runtime/publish" }
$PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
if (!$NativeDirectory) { $NativeDirectory = Join-Path $root 'native/bin' }
$NativeDirectory = [IO.Path]::GetFullPath($NativeDirectory)
$platform = $Runtime.Split('-')[0]
if (($platform -eq 'win' -and !$IsWindows) -or ($platform -eq 'osx' -and !$IsMacOS) -or ($platform -eq 'linux' -and !$IsLinux)) {
    throw "Build $Runtime on its target operating system so native dependencies can be verified."
}
$nativeNames = switch ($platform) {
    win { @('prism_kernel.dll','prism_sim.dll') }
    linux { @('libprism_kernel.so','libprism_sim.so') }
    osx { @('libprism_kernel.dylib','libprism_sim.dylib') }
}
foreach ($name in $nativeNames) {
    if (!(Test-Path -LiteralPath (Join-Path $NativeDirectory $name))) { throw "Missing $name. Build and stage native libraries for $Runtime first." }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if (!$SkipPublish) {
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    $publishArgs = @('publish',(Join-Path $root 'Prism.Desktop/Prism.Desktop.csproj'),'-c','Release','-r',$Runtime,'--self-contained',$selfContained,
        '--artifacts-path',(Join-Path $OutputDirectory 'build'),'-o',$PublishDirectory,"-p:Version=$Version","-p:NativeLibraryDirectory=$NativeDirectory",'-p:PublishTrimmed=false','-p:UseSharedCompilation=false','-m:1')
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    & dotnet publish (Join-Path $root 'Prism.Mcp/Prism.Mcp.csproj') -c Release -r $Runtime --self-contained $selfContained --artifacts-path (Join-Path $OutputDirectory 'build') -o (Join-Path $PublishDirectory 'mcp') "-p:Version=$Version" -p:UseSharedCompilation=false -m:1
    if ($LASTEXITCODE -ne 0) { throw 'MCP publish failed.' }
}
$executable = if ($platform -eq 'win') { 'Prism.Desktop.exe' } else { 'Prism.Desktop' }
if (!(Test-Path -LiteralPath (Join-Path $PublishDirectory $executable))) { throw "No published app in $PublishDirectory" }
foreach ($name in $nativeNames) {
    if (!(Test-Path -LiteralPath (Join-Path $PublishDirectory $name))) { Copy-Item -LiteralPath (Join-Path $NativeDirectory $name) -Destination $PublishDirectory }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $PublishDirectory

switch ($platform) {
    win {
        if (!$Compiler) { $Compiler = (Get-Command makensis -ErrorAction SilentlyContinue).Source }
        if (!$Compiler) { throw 'NSIS compiler not found. Install NSIS or pass -Compiler /path/to/makensis.exe.' }
        # The native C++ shims require the x64 VC runtime. Redistribute the runtime beside the app.
        foreach ($dll in @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll')) {
            $runtimeFile = Join-Path $env:WINDIR "System32/$dll"
            if (!(Test-Path -LiteralPath $runtimeFile)) { throw "Missing VC runtime: $runtimeFile" }
            Copy-Item -LiteralPath $runtimeFile -Destination $PublishDirectory -Force
        }
        $manifest = Join-Path $OutputDirectory "$Runtime/uninstall-files.nsh"
        $lines = [Collections.Generic.List[string]]::new()
        foreach ($file in Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath($PublishDirectory, $file.FullName).Replace('/','\').Replace('$','$$')
            $lines.Add('Delete "$INSTDIR\' + $relative + '"')
        }
        foreach ($dir in Get-ChildItem -LiteralPath $PublishDirectory -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending) {
            $relative = [IO.Path]::GetRelativePath($PublishDirectory, $dir.FullName).Replace('/','\').Replace('$','$$')
            $lines.Add('RMDir "$INSTDIR\' + $relative + '"')
        }
        [IO.File]::WriteAllLines($manifest, $lines)
        $package = Join-Path $OutputDirectory "Prism-$Version-$Runtime-Setup.exe"
        & $Compiler '/V2' "/DVERSION=$Version" "/DPUBLISH_DIR=$PublishDirectory" "/DOUTPUT_FILE=$package" "/DBRAND_ICON=$(Join-Path $root 'Prism/Assets/prism.ico')" "/DUNINSTALL_MANIFEST=$manifest" (Join-Path $PSScriptRoot 'windows/Prism.nsi')
        if ($LASTEXITCODE -ne 0) { throw 'NSIS packaging failed.' }
    }
    osx {
        $bundleRoot = Join-Path $OutputDirectory "$Runtime/bundle"
        $app = Join-Path $bundleRoot 'Prism.app/Contents'
        if (Test-Path -LiteralPath $app) { throw 'Bundle staging already exists. Choose a fresh OutputDirectory.' }
        New-Item -ItemType Directory -Path "$app/MacOS","$app/Resources" -Force | Out-Null
        Copy-Item -Path "$PublishDirectory/*" -Destination "$app/MacOS" -Recurse
        $iconset = Join-Path $OutputDirectory "$Runtime/Prism.iconset"
        New-Item -ItemType Directory -Path $iconset -Force | Out-Null
        foreach ($size in @(16,32,128,256,512)) {
            & sips -z $size $size (Join-Path $root 'Prism/Assets/prism-icon.png') --out "$iconset/icon_${size}x${size}.png" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
            $double = $size * 2
            & sips -z $double $double (Join-Path $root 'Prism/Assets/prism-icon.png') --out "$iconset/icon_${size}x${size}@2x.png" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
        }
        & iconutil --convert icns $iconset --output "$app/Resources/Prism.icns"
        if ($LASTEXITCODE -ne 0) { throw 'Icon bundling failed.' }
        # Relocate bundled dependency references instead of retaining build-machine paths.
        foreach ($lib in Get-ChildItem -LiteralPath "$app/MacOS" -Filter '*.dylib') {
            & install_name_tool -id "@rpath/$($lib.Name)" $lib.FullName
            $dependencies = & otool -L $lib.FullName
            foreach ($line in $dependencies | Select-Object -Skip 1) {
                $dependency = ($line.Trim() -split '\s+')[0]
                $localName = Split-Path $dependency -Leaf
                if (Test-Path -LiteralPath "$app/MacOS/$localName") { & install_name_tool -change $dependency "@loader_path/$localName" $lib.FullName }
            }
        }
        $plist = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'macos/Info.plist') -Raw
        [IO.File]::WriteAllText("$app/Info.plist", $plist.Replace('@VERSION@',$Version))
        & chmod '+x' "$app/MacOS/Prism.Desktop"
        # Ad-hoc signing makes local testing possible; public releases still need Developer ID/notarization.
        & codesign --force --deep --sign '-' (Split-Path $app -Parent)
        if ($LASTEXITCODE -ne 0) { throw 'Ad-hoc app signing failed.' }
        New-Item -ItemType SymbolicLink -Path (Join-Path $bundleRoot 'Applications') -Target /Applications | Out-Null
        $package = Join-Path $OutputDirectory "Prism-$Version-$Runtime.dmg"
        & hdiutil create -volname Prism -srcfolder $bundleRoot -ov -format UDZO $package
        if ($LASTEXITCODE -ne 0) { throw 'DMG packaging failed.' }
    }
    linux {
        foreach ($name in $nativeNames) {
            $dependencies = & ldd (Join-Path $PublishDirectory $name)
            if ($LASTEXITCODE -ne 0 -or $dependencies -match 'not found') { throw "Unresolved native dependencies for $name" }
        }
        $deb = Join-Path $OutputDirectory "$Runtime/deb"
        if (Test-Path -LiteralPath $deb) { throw 'DEB staging already exists. Choose a fresh OutputDirectory.' }
        New-Item -ItemType Directory -Path "$deb/opt/prism","$deb/usr/bin","$deb/usr/share/applications","$deb/usr/share/icons/hicolor/256x256/apps","$deb/DEBIAN" -Force | Out-Null
        Copy-Item -Path "$PublishDirectory/*" -Destination "$deb/opt/prism" -Recurse
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'linux/prism') -Destination "$deb/usr/bin/prism"
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'linux/prism.desktop') -Destination "$deb/usr/share/applications/prism.desktop"
        Copy-Item -LiteralPath (Join-Path $root 'Prism/Assets/prism-icon.png') -Destination "$deb/usr/share/icons/hicolor/256x256/apps/prism.png"
        $control = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'linux/control') -Raw
        [IO.File]::WriteAllText("$deb/DEBIAN/control", $control.Replace('@VERSION@',$Version))
        & chmod 755 "$deb/usr/bin/prism" "$deb/opt/prism/Prism.Desktop"
        $package = Join-Path $OutputDirectory "prism_${Version}_amd64.deb"
        & dpkg-deb --build --root-owner-group $deb $package
        if ($LASTEXITCODE -ne 0) { throw 'DEB packaging failed.' }
    }
}
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$package.sha256", "$hash  $(Split-Path $package -Leaf)`n")
Write-Output "Created $package"
