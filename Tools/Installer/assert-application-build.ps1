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
# The test host's methods must not be in an application build's binaries either (spec 5.1: not only hidden at run
# time). Their names are string literals: UTF-16 in .NET metadata, UTF-8 anywhere else.
$names = @('test.barrier', 'test.editor.pageText', 'test.window.open', 'test.settings.set', 'test.backup.run')
# A list, not pipeline output: PowerShell would unroll each byte array into single bytes.
$patterns = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($name in $names) { $patterns.Add([Text.Encoding]::Unicode.GetBytes($name)); $patterns.Add([Text.Encoding]::UTF8.GetBytes($name)) }
function Test-Contains([byte[]]$data, [byte[]]$pattern) {
    $first = $pattern[0]
    $i = [Array]::IndexOf($data, $first)
    while ($i -ge 0 -and $i -le $data.Length - $pattern.Length) {
        $match = $true
        for ($j = 1; $j -lt $pattern.Length; $j++) { if ($data[$i + $j] -ne $pattern[$j]) { $match = $false; break } }
        if ($match) { return $true }
        $i = [Array]::IndexOf($data, $first, $i + 1)
    }
    return $false
}
$binaries = Get-ChildItem -Path $Path -Recurse -File -Include *.dll, *.exe -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'Typedown*' }
foreach ($binary in $binaries) {
    $data = [IO.File]::ReadAllBytes($binary.FullName)
    for ($k = 0; $k -lt $names.Count; $k++) {
        if ((Test-Contains $data $patterns[2 * $k]) -or (Test-Contains $data $patterns[2 * $k + 1])) {
            throw "Refusing to package: $($binary.Name) contains the test host method '$($names[$k])'."
        }
    }
}
Write-Host "assert-application-build: $Path is an application build ($($binaries.Count) Typedown binaries scanned)"
