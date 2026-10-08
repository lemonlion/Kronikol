#requires -Version 7
<#
.SYNOPSIS
  The perf check: 2,000 trivial facts (20 classes x 100, each awaiting Task.Yield then making one tracked
  call) under the prototype framework with PROBE_SYNC=1 and PROBE_SYNC=0, and under Kronikol.xUnit2's own
  ReportingTestFramework for reference. Each round runs C sync on, C sync off, then A, so the two C lanes
  alternate and drift on a busy machine spreads over all three.

.DESCRIPTION
  One row per run goes to results/perf/perf.tsv (tools/perf_row.py): wall time of `dotnet test --no-build`,
  xUnit's own timings and the report generation time; tools/summarize.py takes medians and ranges.
  Console logs, TRX files and reports stay under .build/perf-logs and the build output (2,000 results each).

.PARAMETER Rounds
  Rounds of the three lanes. Default 5.
#>
param([int]$Rounds = 5)

$ErrorActionPreference = 'Stop'
$H = $PSScriptRoot
$Out = Join-Path $H 'results/perf'
$Logs = Join-Path $H '.build/perf-logs'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$env:PYTHONUTF8 = '1'
$env:KRONIKOL_HISTORY = 'off'

& python "$H/tools/gen_perf.py"
foreach ($p in 'D.Perf', 'D.PerfA') {
    $log = & dotnet build "$H/probe/$p/$p.csproj" --artifacts-path "$H/.build/main" -c Debug 2>&1
    if ($LASTEXITCODE -ne 0) { $log | Select-Object -Last 30 | Write-Host; throw "build failed: $p" }
}

Remove-Item $Out, $Logs -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $Out, $Logs | Out-Null
& python "$H/tools/perf_row.py" --header | Set-Content "$Out/perf.tsv"

$lanes = @(
    @{ Lane = 'C-sync1'; Project = 'D.Perf'; Sync = '1' },
    @{ Lane = 'C-sync0'; Project = 'D.Perf'; Sync = '0' },
    @{ Lane = 'A'; Project = 'D.PerfA'; Sync = $null }
)
for ($r = 1; $r -le $Rounds; $r++) {
    foreach ($l in $lanes) {
        $name = "$($l.Lane)-$r"
        $bin = "$H/.build/main/bin/$($l.Project)/debug"
        Remove-Item "$bin/Reports" -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item Env:PROBE_SYNC, Env:PROBE_TIMING, Env:PROBE_LOG -ErrorAction SilentlyContinue
        if ($l.Sync) { $env:PROBE_SYNC = $l.Sync; $env:PROBE_TIMING = "$Logs/$name.timing.txt" }
        Write-Host "perf $name"
        $clock = [Diagnostics.Stopwatch]::StartNew()
        & dotnet test "$H/probe/$($l.Project)/$($l.Project).csproj" --no-build --artifacts-path "$H/.build/main" `
            --logger "trx;LogFileName=$name.trx" --logger 'console;verbosity=normal' --results-directory $Logs *> "$Logs/$name.console.txt"
        $exit = $LASTEXITCODE
        $clock.Stop()
        Remove-Item Env:PROBE_SYNC, Env:PROBE_TIMING -ErrorAction SilentlyContinue
        & python "$H/tools/perf_row.py" --lane $l.Lane --round $r --exit $exit --wall $clock.Elapsed.TotalSeconds `
            --console "$Logs/$name.console.txt" --trx "$Logs/$name.trx" --timing "$Logs/$name.timing.txt" --reports "$bin/Reports" |
            Add-Content "$Out/perf.tsv"
    }
}
Get-Content "$Out/perf.tsv"
& python "$H/tools/summarize.py" (Join-Path $H 'results')
