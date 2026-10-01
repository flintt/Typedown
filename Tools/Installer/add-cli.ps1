<#
.SYNOPSIS
    Adds typedownctl (the automation CLI, and "typedownctl mcp", the MCP server) and the automation documents to an
    app folder about to be packaged. Called by build-local.ps1 and the CI packaging step.

.DESCRIPTION
    typedownctl is built for netcoreapp3.1, self-contained, for the app's own runtime identifier: its runtime files
    are the ones the app already carries, so the installer grows by a few hundred KB instead of a second runtime.
    Every file the CLI brings is checked against the app folder:
      - not there yet: copied;
      - there and byte for byte the same: left alone;
      - there and different: the build stops. A different runtime file would replace the app's own. The only
        exceptions are the libraries both are compiled against (Typedown.Automation, Newtonsoft.Json): the app's
        copy stays, provided the assembly version is the same.
    Then typedownctl.exe is run from the app folder ("help") to prove it starts on the app's runtime (not for an
    ARM64 build on an x64 machine, which cannot start it), and the
    documents go to <app>\docs with their relative links intact.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools\Installer\add-cli.ps1 -AppFolder Tools\Installer\publish -RuntimeIdentifier win10-x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AppFolder,
    [Parameter(Mandatory = $true)][string]$RuntimeIdentifier,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$AppFolder = (Resolve-Path $AppFolder).Path
if (-not (Test-Path (Join-Path $AppFolder 'Typedown.exe'))) { throw "add-cli: no Typedown.exe in $AppFolder" }

$out = Join-Path ([IO.Path]::GetTempPath()) ("typedownctl-" + [Guid]::NewGuid().ToString('N'))
# The portable identifier (win-x64 for the app's win10-x64): the project also targets net8.0, which no longer knows
# the version-specific ones, and both name the same runtime pack - the file comparison below proves it.
$rid = $RuntimeIdentifier -replace '^win10-', 'win-'
Write-Host "add-cli: publishing typedownctl for netcoreapp3.1 / $rid"
& dotnet publish (Join-Path $repo 'Tools\Typedown.Cli\Typedown.Cli.csproj') -f netcoreapp3.1 -r $rid `
    --self-contained true -c $Configuration -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "add-cli: dotnet publish failed" }

function Get-AssemblyVersion([string]$file) {
    try { [Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString() } catch { $null }
}

$shared = @('Typedown.Automation.dll', 'Newtonsoft.Json.dll')
$added = 0; $same = 0; $kept = 0
try {
    foreach ($file in Get-ChildItem $out -Recurse -File) {
        $relative = $file.FullName.Substring($out.Length).TrimStart('\', '/')
        if ($relative -like '*.pdb') { continue }
        $target = Join-Path $AppFolder $relative
        if (-not (Test-Path $target)) {
            New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
            Copy-Item $file.FullName $target
            $added++
            continue
        }
        if ((Get-FileHash $file.FullName).Hash -eq (Get-FileHash $target).Hash) { $same++; continue }
        if ($shared -contains $file.Name) {
            $mine = Get-AssemblyVersion $file.FullName
            $theirs = Get-AssemblyVersion $target
            if ($mine -and $mine -eq $theirs) { $kept++; continue }
            throw "add-cli: $relative is version $mine for typedownctl but $theirs in the app"
        }
        throw "add-cli: typedownctl would replace the app's $relative with a different file - the runtime typedownctl was built for is not the app's. Build both with the same .NET SDK."
    }
}
finally {
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "add-cli: $added file(s) added, $same already there and identical, $kept shared librar(ies) kept from the app"

# It has to start on the app's runtime, from the app folder - where this machine can run it: an ARM64 build made on an
# x64 machine (CI builds both there) cannot start, and the release build stopped here. The file checks above hold for
# it all the same.
$hostArch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
if ($rid -like '*-arm64' -and $hostArch -ne 'arm64') {
    Write-Host "add-cli: typedownctl.exe not started: $rid does not run on this $hostArch machine"
}
else {
    $help = & (Join-Path $AppFolder 'typedownctl.exe') help 2>&1
    if ($LASTEXITCODE -ne 0 -or -not ($help -join "`n").Contains('typedownctl')) {
        throw "add-cli: typedownctl.exe does not run from the app folder (exit $LASTEXITCODE): $help"
    }
    Write-Host "add-cli: typedownctl.exe runs from the app folder"
}

# The documents, keeping their relative links: automation.md links the spec, the schema, the examples and the MCP page.
$docs = Join-Path $AppFolder 'docs'
New-Item -ItemType Directory -Force $docs, (Join-Path $docs 'automation-schema'), (Join-Path $docs 'automation-examples') | Out-Null
foreach ($name in 'automation.md', 'automation-mcp.md', 'automation-api-spec.md') {
    Copy-Item (Join-Path $repo "docs\$name") $docs -Force
}
Copy-Item (Join-Path $repo 'docs\automation-schema\v1.json') (Join-Path $docs 'automation-schema') -Force
Get-ChildItem (Join-Path $repo 'docs\automation-examples') -File -Include *.py, *.ps1, *.md -Recurse |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $docs 'automation-examples') -Force }
Write-Host "add-cli: documents in $docs"
