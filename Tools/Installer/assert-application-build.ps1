<#
    Refuses to package anything that is not the application: an automation test host build (the test.* methods,
    edit barriers and its own data directory; see docs/automation-api-analysis-plan.md) must never reach an
    installer, MSIX or portable zip. Called by build-local.ps1 and the CI packaging step on the folder about to be
    packaged; throws when it finds the test host's assembly or marker.
#>
param([Parameter(Mandatory = $true)][string]$Path)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Path)) { throw "assert-application-build: '$Path' does not exist" }
$found = Get-ChildItem -Path $Path -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'Typedown.Automation.TestHost*' -or $_.Name -eq 'automation-test-host.marker' }
if ($found) {
    $list = ($found | ForEach-Object { $_.FullName }) -join "`n  "
    throw "Refusing to package an automation test host build. Found:`n  $list"
}
Write-Host "assert-application-build: $Path is an application build"
