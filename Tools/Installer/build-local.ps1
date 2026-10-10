<#
.SYNOPSIS
    Builds the Windows app and packs it into the Inno Setup installer, the same way CI does, on a machine with
    the Build Tools installed.

.DESCRIPTION
    Three steps: the .NET SDK publishes Dev\Typedown (self-contained with the Windows App SDK runtime, per
    architecture, precompiled with ReadyToRun and trimmed as in CI), the output is copied to Tools\Installer\publish,
    and ISCC compiles Tools\Installer\Typedown.iss over it. -AutomationTestHost only builds (the E2E scripts run the
    test host from its bin folder). The result is Tools\Installer\Output\Typedown-windows-<arch>-v<version>.exe.

    Unlike CI it does not sign anything — CI signs with a self-signed certificate the system does not trust
    either, so for testing on your own machine the difference is one more SmartScreen prompt.

    What it needs, and where it looks:
      .NET 10 SDK    dotnet on PATH, or the one TYPEDOWN_DOTNET names (an SDK unpacked outside Program Files).
      Inno Setup 6   ISCC.exe, either the per-user install (%LOCALAPPDATA%\Programs\Inno Setup 6, which needs
                     no administrator) or the machine-wide one.
      Editor bundle  Dev\Typedown\Resources\Statics, which is not in the repository: build it with
                     "npm ci && npm run build" in Dev\Typedown.Editor, or copy it from another machine.
                     The script refuses to package a bundle older than the editor sources, so a change to the
                     editor cannot quietly stay out of the installer; -BuildEditor rebuilds it here instead.

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
    [switch]$NoInstaller,
    # What to show in About instead of "local.<commit>"; empty keeps whatever the file already says.
    [string]$Label,
    # Rebuilds the editor bundle first (needs node and Dev\Typedown.Editor\node_modules).
    [switch]$BuildEditor,
    # Builds the automation test host instead (the app with test.* methods and edit barriers, its own data root) into
    # Dev\Typedown\bin\AutomationTestHost. Implies -NoInstaller: a test host is never packaged.
    [switch]$AutomationTestHost
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $repo

function Find-Dotnet {
    $dotnet = if ($env:TYPEDOWN_DOTNET) { $env:TYPEDOWN_DOTNET } else { 'dotnet' }
    $sdks = & $dotnet --list-sdks 2>$null
    if ($LASTEXITCODE -ne 0 -or -not ($sdks | Where-Object { $_ -match '^10\.' })) {
        throw "no .NET 10 SDK found ($dotnet) - install it, or set TYPEDOWN_DOTNET to its dotnet.exe"
    }
    $dotnet
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

$editorDir = Join-Path $repo 'Dev\Typedown.Editor'
$statics = Join-Path $repo 'Dev\Typedown\Resources\Statics\index.html'

if ($BuildEditor) {
    if (-not (Test-Path (Join-Path $editorDir 'node_modules'))) {
        throw "the editor has no dependencies installed - run 'npm ci' (or 'yarn install') in Dev\Typedown.Editor first"
    }
    Write-Host 'Building the editor bundle'
    Push-Location $editorDir
    try {
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw "the editor build failed" }
    } finally { Pop-Location }
}

if (-not (Test-Path $statics)) {
    throw "the editor bundle is missing (Dev\Typedown\Resources\Statics) - build it in Dev\Typedown.Editor with npm run build, or copy it from a machine that has it"
}

# A bundle older than the editor sources would mean packaging an app whose editor is a version behind, which
# is silent and very easy to miss: the installer builds, runs, and simply does not contain the change.
$builtAt = (Get-Item $statics).LastWriteTimeUtc
$newestSource = Get-ChildItem (Join-Path $editorDir 'src') -Recurse -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if ($newestSource -and $newestSource.LastWriteTimeUtc -gt $builtAt) {
    $rel = $newestSource.FullName.Substring($repo.Length).TrimStart('\')
    throw "the editor bundle is older than the editor sources ($rel changed after it was built) - rerun with -BuildEditor, or copy a fresh bundle from the machine that built it"
}

$version = ([xml](Get-Content 'Dev\Typedown\Typedown.csproj')).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
$arch = $Platform.ToLowerInvariant()
Write-Host "Typedown $version  $Platform  $Configuration"

# About shows this next to the version. CI stamps its run and commit here, so a local build says where it came
# from too rather than carrying whatever label the repository was committed with.
function Get-LocalLabel {
    if ($Label) { return $Label }
    try {
        $commit = (git rev-parse --short HEAD 2>$null)
        if ($LASTEXITCODE -ne 0 -or -not $commit) { return "local.$(Get-Date -Format yyyyMMdd.HHmm)" }
        $dirty = (git status --porcelain 2>$null | Measure-Object).Count -gt 0
        return "local.$commit" + $(if ($dirty) { '+dirty' } else { '' })
    }
    catch {
        return "local.$(Get-Date -Format yyyyMMdd.HHmm)"
    }
}

$rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }

if (-not $SkipBuild) {
    $dotnet = Find-Dotnet
    Write-Host "dotnet: $dotnet"

    # The label lives in a source file, so it is put back afterwards: building must not leave the checkout
    # modified, or the next build would call itself dirty and a commit could carry the label by accident.
    $configPath = Join-Path $repo 'Dev\Typedown.Core\Config.cs'
    $configOriginal = Get-Content $configPath -Raw
    $label = Get-LocalLabel
    Write-Host "Label: $label"
    $stamped = [regex]::Replace($configOriginal, 'public const string TestBuild = "[^"]*";', "public const string TestBuild = `"$label`";")
    if ($stamped -eq $configOriginal) { Write-Warning "TestBuild not found in Config.cs; building without a label" }
    try {
        Set-Content $configPath $stamped -Encoding utf8 -NoNewline
        # The application is published, as CI does: precompiled (ReadyToRun) and trimmed (TypedownTrim, Typedown.csproj).
        # The test host is only built - the E2E scripts run it from its bin folder.
        if ($AutomationTestHost) {
            & $dotnet build 'Dev\Typedown\Typedown.csproj' -nologo -v m -c $Configuration -p:Platform=$Platform -p:AutomationTestHost=true
        }
        else {
            & $dotnet publish 'Dev\Typedown\Typedown.csproj' -nologo -v m -c $Configuration -p:Platform=$Platform -r $rid `
                -p:TypedownTrim=true -o (Join-Path $repo "Dev\Typedown\bin\$Platform\$Configuration\publish")
        }
        $buildFailed = $LASTEXITCODE -ne 0
    }
    finally {
        Set-Content $configPath $configOriginal -Encoding utf8 -NoNewline
    }
    if ($buildFailed) { throw "build failed" }
}

if ($AutomationTestHost) {
    $published = Join-Path $repo "Dev\Typedown\bin\AutomationTestHost\$Platform\$Configuration\net10.0-windows10.0.26100.0\$rid"
    if (-not (Test-Path (Join-Path $published 'automation-test-host.marker'))) { throw "no test host marker in $published" }
    Write-Host "Automation test host: $published"
    return
}
$published = Join-Path $repo "Dev\Typedown\bin\$Platform\$Configuration\publish"
$exe = ([xml](Get-Content (Join-Path $repo 'Branding.props'))).Project.PropertyGroup.BrandExeName + '.exe'
if (-not (Test-Path (Join-Path $published $exe))) { throw "$exe not found in $published" }
Write-Host "App: $published"
& (Join-Path $PSScriptRoot 'assert-application-build.ps1') -Path $published
if ($NoInstaller) { return }

$iscc = Find-ISCC
$stage = Join-Path $repo 'Tools\Installer\publish'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
# Not a runtime-specific build left beside it (win-x64\...): nothing the installer uses.
Get-ChildItem $published | Where-Object { -not ($_.PSIsContainer -and $_.Name -match '^win-(x64|x86|arm64)$') } |
    Copy-Item -Destination $stage -Recurse -Force
# The CLI (and its MCP server) is published with the app; this checks it starts and adds the automation documents.
& (Join-Path $PSScriptRoot 'add-cli.ps1') -AppFolder $stage -RuntimeIdentifier $rid -Configuration $Configuration
& (Join-Path $PSScriptRoot 'assert-application-build.ps1') -Path $stage

& $iscc /DMyAppVersion=$version /DMyArch=$arch 'Tools\Installer\Typedown.iss' | Select-String -Pattern 'error|Successful compile' | ForEach-Object { $_.Line }
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$setup = Get-ChildItem 'Tools\Installer\Output\*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ("Installer: {0}  {1:N1} MB" -f $setup.FullName, ($setup.Length / 1MB))
