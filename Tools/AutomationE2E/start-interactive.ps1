<#
.SYNOPSIS
    From SSH: runs run-windows-e2e.ps1 in the logged-on user's desktop session through a scheduled task
    ("run only when the user is logged on"), waits for it, prints result.json and exits with its code.
    Without a logged-on session the task never produces a result: that is reported as an environment error (3).
#>
param(
    [Parameter(Mandatory = $true)][string]$TestHost,
    [Parameter(Mandatory = $true)][string]$Driver,
    [Parameter(Mandatory = $true)][string]$Artifacts,
    [int]$TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
$task = 'TypedownAutomationE2E'
$script = Join-Path $PSScriptRoot 'run-windows-e2e.ps1'
New-Item -ItemType Directory -Force $Artifacts | Out-Null
$before = @(Get-ChildItem $Artifacts -Directory | ForEach-Object { $_.Name })
# Hidden: a console window on the desktop would cover the test host and take the foreground from it.
$command = "powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`" -TestHost `"$TestHost`" -Driver `"$Driver`" -Artifacts `"$Artifacts`""
schtasks /Create /TN $task /TR $command /SC ONCE /ST 23:59 /IT /F | Out-Null
schtasks /Run /TN $task | Out-Null
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$run = $null
while ((Get-Date) -lt $deadline) {
    $run = Get-ChildItem $Artifacts -Directory | Where-Object { $before -notcontains $_.Name } | Select-Object -First 1
    if ($run -and (Test-Path (Join-Path $run.FullName 'done.txt'))) { break }
    Start-Sleep -Seconds 2
}
if (-not $run -or -not (Test-Path (Join-Path $run.FullName 'done.txt'))) {
    Write-Output "ENVIRONMENT ERROR: no result from the interactive task within $TimeoutSeconds s (nobody logged on, or the desktop is locked)"
    exit 3
}
$result = Join-Path $run.FullName 'result.json'
if (Test-Path $result) { Get-Content $result -Raw -Encoding utf8 } else { Get-Content (Join-Path $run.FullName 'driver.log') -Raw -ErrorAction SilentlyContinue }
$exit = [int]((Get-Content (Join-Path $run.FullName 'done.txt')) -replace 'exit=', '')
Write-Output "artifacts: $($run.FullName)"
exit $exit
