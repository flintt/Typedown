[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release", "Debug_Local")]
    [string]$Configuration = "Debug_Local",

    [ValidateSet("x64", "ARM64")]
    [string[]]$Platform = @("x64", "ARM64"),

    [ValidateSet("Auto", "Always", "Never")]
    [string]$Restore = "Auto"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$project = Join-Path $repositoryRoot "Dev\Typedown.WinUI\Typedown.WinUI.csproj"

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    Write-Error "WinUI project not found: $project"
    exit 2
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    Write-Error "dotnet was not found on PATH. Install the required .NET 10 SDK outside this script."
    exit 127
}

$commit = (& git -C $repositoryRoot rev-parse --verify HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
    $commit = "unknown"
}
else {
    $commit = $commit.Trim()
}

$firstFailure = 0

foreach ($target in $Platform) {
    Write-Host "WinUI build: commit=$commit configuration=$Configuration platform=$target restore=$Restore"

    if ($Restore -eq "Always") {
        & $dotnet.Source restore $project `
            --property:Configuration=$Configuration `
            --property:Platform=$target `
            --nologo
        $restoreExitCode = $LASTEXITCODE
        if ($restoreExitCode -ne 0) {
            Write-Error "WinUI restore failed: platform=$target exitCode=$restoreExitCode" -ErrorAction Continue
            if ($firstFailure -eq 0) {
                $firstFailure = $restoreExitCode
            }
            continue
        }
    }

    $buildArguments = @(
        "build",
        $project,
        "--configuration", $Configuration,
        "--property:Platform=$target",
        "--nologo"
    )
    if ($Restore -ne "Auto") {
        $buildArguments += "--no-restore"
    }

    & $dotnet.Source @buildArguments
    $buildExitCode = $LASTEXITCODE
    if ($buildExitCode -ne 0) {
        Write-Error "WinUI build failed: platform=$target exitCode=$buildExitCode" -ErrorAction Continue
        if ($firstFailure -eq 0) {
            $firstFailure = $buildExitCode
        }
    }
}

exit $firstFailure
