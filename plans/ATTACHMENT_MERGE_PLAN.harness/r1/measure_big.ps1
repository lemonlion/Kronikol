# The issue's scale on a build: one ingest of the probe's @big run, with its own temp directory so the staging folders
# it leaves are its own. Prints the exit code, the time, the peak working set, what attachments/ holds and what is left.
# usage (from probe/, after `PROBE_OUT=out-big npx playwright test --grep @big`):
#   pwsh -File measure_big.ps1 <Kronikol.Tool.dll> <label>
param([string]$Dll, [string]$Label)
$ErrorActionPreference = 'Stop'
$work = Join-Path (Get-Location) "big-$Label"
if (Test-Path $work) { cmd /c rmdir /s /q "$work" }
$temp = Join-Path $work 'temp'
New-Item -ItemType Directory -Force $temp | Out-Null
Set-Content -Path (Join-Path $work 'empty.ndjson') -Value '' -NoNewline

$info = New-Object System.Diagnostics.ProcessStartInfo 'dotnet'
foreach ($a in @($Dll, 'ingest', (Join-Path $work 'empty.ndjson'), '--tests', 'out-big/tests.jsonl',
                 '--cucumber-messages', 'out-big/messages.ndjson', '-o', (Join-Path $work 'reports'))) { $info.ArgumentList.Add($a) }
$info.UseShellExecute = $false
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$info.Environment['TMP'] = $temp
$info.Environment['TEMP'] = $temp
$watch = [System.Diagnostics.Stopwatch]::StartNew()
$p = [System.Diagnostics.Process]::Start($info)
$stdout = $p.StandardOutput.ReadToEndAsync()
$stderr = $p.StandardError.ReadToEndAsync()
$peak = 0
while (-not $p.HasExited) {
    try { $p.Refresh(); if ($p.PeakWorkingSet64 -gt $peak) { $peak = $p.PeakWorkingSet64 } } catch { }
    Start-Sleep -Milliseconds 20
}
$p.WaitForExit()
$watch.Stop()

$attachments = Join-Path $work 'reports/attachments'
$files = @(Get-ChildItem $attachments -File -ErrorAction SilentlyContinue)
$size = ($files | Measure-Object -Sum Length).Sum
$staging = Join-Path $temp 'kronikol-cucumber-attachments'
$left = @(Get-ChildItem $staging -Directory -ErrorAction SilentlyContinue)
$leftSize = (Get-ChildItem $staging -File -Recurse -ErrorAction SilentlyContinue | Measure-Object -Sum Length).Sum
'{0}: exit {1}, {2:0.0} s, peak {3:N0} MB, attachments/ {4} files {5:0.0} MB, staging folders left {6} ({7:0.0} MB)' -f `
    $Label, $p.ExitCode, $watch.Elapsed.TotalSeconds, ($peak / 1MB), $files.Count, ($size / 1MB), $left.Count, ($leftSize / 1MB)
