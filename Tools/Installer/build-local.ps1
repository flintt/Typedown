<#
.SYNOPSIS
    Builds the Windows app and packs it into the Inno Setup installer, the same way CI does, on a machine with
    the Build Tools installed.

.DESCRIPTION
    Three steps: MSBuild builds Dev\Typedown (self-contained, per architecture), the output is copied to
    Tools\Installer\publish, and ISCC compiles Tools\Installer\Typedown.iss over it. The result is
    Tools\Installer\Output\Typedown-windows-<arch>-v<version>.exe.

    Unlike CI it does not sign anything — CI signs with a self-signed certificate the system does not trust
    either, so for testing on your own machine the difference is one more SmartScreen prompt.

    What it needs, and where it looks:
      MSBuild        Visual Studio 2022 Build Tools with the "UWP" and ".NET desktop" build tool workloads,
                     found through vswhere.
      Windows SDK    read from the KitsRoot10 registry value, so an SDK installed outside Program Files is
                     found too; mt.exe and makepri.exe from the newest version present are passed to MSBuild,
                     which otherwise looks for them under Program Files only and fails with MSB3073.
      Inno Setup 6   ISCC.exe, either the per-user install (%LOCALAPPDATA%\Programs\Inno Setup 6, which needs
                     no administrator) or the machine-wide one.
      Editor bundle  Dev\Typedown\Resources\Statics, which is not in the repository: build it with
                     "npm ci && npm run build" in Dev\Typedown.Editor, or copy it from another machine.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools\Installer\build-local.ps1
    powershell -ExecutionPolicy Bypass -File Tools\Installer\build-local.ps1 -Platform ARM64 -SkipBuild
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')][string]$Platform = 'x64',
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    # Packs whatever was built last instead of building again.
    [switch]$SkipBuild,
    # Stops after the build, without making an installer.
    [switch]$NoInstaller
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $repo

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw "vswhere not found - install Visual Studio 2022 Build Tools (see the comment at the top of this script)" }
    $install = & $vswhere -products * -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1
    if (-not $install) { throw "no Visual Studio installation with MSBuild found" }
    $msbuild = Join-Path $install 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path $msbuild)) { throw "MSBuild not found under $install" }
    $msbuild
}

function Find-SdkTools {
    $root = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots' -ErrorAction SilentlyContinue).KitsRoot10
    if (-not $root) { $root = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10' }
    $bin = Join-Path $root 'bin'
    if (-not (Test-Path $bin)) { throw "no Windows SDK under $root" }
    $version = Get-ChildItem $bin -Directory |
        Where-Object { $_.Name -match '^10\.' -and (Test-Path (Join-Path $_.FullName 'x86\mt.exe')) } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $version) { throw "no Windows SDK with mt.exe under $bin" }
    @{
        ManifestTool = Join-Path $version.FullName 'x86\mt.exe'
        MakePri      = Join-Path $version.FullName 'x86\makepri.exe'
        Version      = $version.Name
    }
}

function Find-ISCC {
    foreach ($p in @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))) {
        if (Test-Path $p) { return $p }
    }
    throw "ISCC.exe not found - install Inno Setup 6 (innosetup-6.x.exe /VERYSILENT /CURRENTUSER needs no administrator)"
}

$statics = Join-Path $repo 'Dev\Typedown\Resources\Statics\index.html'
if (-not (Test-Path $statics)) {
    throw "the editor bundle is missing (Dev\Typedown\Resources\Statics) - build it in Dev\Typedown.Editor with npm run build, or copy it from a machine that has it"
}

$version = ([xml](Get-Content 'Dev\Typedown\Typedown.csproj')).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
$arch = $Platform.ToLowerInvariant()
Write-Host "Typedown $version  $Platform  $Configuration"

if (-not $SkipBuild) {
    $msbuild = Find-MSBuild
    $sdk = Find-SdkTools
    Write-Host "MSBuild: $msbuild"
    Write-Host "Windows SDK: $($sdk.Version)"
    & $msbuild 'Dev\Typedown\Typedown.csproj' /t:Restore,Build /m /v:m `
        /p:Configuration=$Configuration /p:Platform=$Platform `
        /p:ManifestTool=$($sdk.ManifestTool) /p:MakePri=$($sdk.MakePri)
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
}

$rid = if ($Platform -eq 'ARM64') { 'win10-arm64' } else { 'win10-x64' }
$published = Join-Path $repo "Dev\Typedown\bin\$Platform\$Configuration\netcoreapp3.1\$rid"
if (-not (Test-Path (Join-Path $published 'Typedown.exe'))) { throw "Typedown.exe not found in $published" }
Write-Host "App: $published"
if ($NoInstaller) { return }

$iscc = Find-ISCC
$stage = Join-Path $repo 'Tools\Installer\publish'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $published '*') $stage -Recurse -Force

& $iscc /DMyAppVersion=$version /DMyArch=$arch 'Tools\Installer\Typedown.iss' | Select-String -Pattern 'error|Successful compile' | ForEach-Object { $_.Line }
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$setup = Get-ChildItem 'Tools\Installer\Output\*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ("Installer: {0}  {1:N1} MB" -f $setup.FullName, ($setup.Length / 1MB))
