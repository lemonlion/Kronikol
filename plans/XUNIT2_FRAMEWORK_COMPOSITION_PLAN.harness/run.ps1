#requires -Version 7
<#
.SYNOPSIS
  Builds the probes and runs every lane, saving each run's evidence under results/<lane>/<n>/, then writes
  results/SUMMARY.md (tools/summarize.py). The perf check is perf.ps1.

.DESCRIPTION
  Build output goes to .build/<lane>/ (--artifacts-path), so nothing is written under src/. KRONIKOL_HISTORY
  is set to "off": a run under plans/ otherwise finds the repository and appends its line to the
  working tree's .kronikol/history.jsonl.

  Per run: console.txt (dotnet test, console logger at normal verbosity), run.trx, compare.txt and
  compare.json (tools/compare.py), Failures.md and Run.json copied from the report directory,
  reports-files.txt (the report directory's file list with sizes), any kronikol-error.log,
  prototype-error.log or prototype-formatter.log from the test output (saved as <name>.log.txt), run-info.json (environment, exit
  code, wall time), and for C and F: asynclocal.tsv, asynclocal.scenarios.tsv, asynclocal.txt/json.

.PARAMETER Runs
  How many times the repeated lanes (A, C sync on and off, guarded and unguarded, E, A3) run. Default 3.
.PARAMETER Lanes
  Run only these lanes (names as in results/). Default: all.
.PARAMETER CompareOnly
  Run nothing: compare every saved run again from its run.trx and report-extract.json, then summarize.
#>
param(
    [int]$Runs = 3,
    [string[]]$Lanes,
    [switch]$CompareOnly
)

$ErrorActionPreference = 'Stop'
# pwsh -File hands "-Lanes a,b" over as the one string "a,b".
if ($Lanes) { $Lanes = @($Lanes | ForEach-Object { $_ -split ',' } | Where-Object { $_ }) }
$H = $PSScriptRoot
$Results = Join-Path $H 'results'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$env:PYTHONUTF8 = '1'
$env:KRONIKOL_HISTORY = 'off'
$ProbeVars = 'PROBE_SYNC', 'PROBE_FORMAT_GUARD', 'PROBE_LOG', 'PROBE_TIMING', 'PROBE_REPORTS'

function Build([string]$Project, [string]$Artifacts = 'main', [string[]]$Extra = @()) {
    Write-Host "build $Project ($Artifacts) $Extra"
    $log = & dotnet build "$H/probe/$Project/$Project.csproj" --artifacts-path "$H/.build/$Artifacts" -c Debug @Extra 2>&1
    if ($LASTEXITCODE -ne 0) { $log | Select-Object -Last 30 | Write-Host; throw "build failed: $Project" }
}

function Want([string]$Lane) { -not $Lanes -or $Lanes -contains $Lane }

function Invoke-Lane {
    param([string]$Lane, [string]$Project, [int]$N, [hashtable]$Vars = @{}, [string]$Artifacts = 'main')
    if (-not (Want $Lane)) { return }
    $out = Join-Path $Results "$Lane/$N"
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $out | Out-Null
    $bin = "$H/.build/$Artifacts/bin/$Project/debug"
    Remove-Item "$bin/Reports" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "$bin/kronikol-error.log", "$bin/prototype-error.log", "$bin/prototype-formatter.log", "$bin/starttime-probe.txt" -Force -ErrorAction SilentlyContinue

    foreach ($v in $ProbeVars) { Remove-Item "Env:$v" -ErrorAction SilentlyContinue }
    foreach ($k in $Vars.Keys) { Set-Item "Env:$k" $Vars[$k] }
    if ($Project -in 'C.AssemblyFixturePrototype', 'F.DependencyInjectionPrototype') { $env:PROBE_LOG = Join-Path $out 'asynclocal.tsv' }

    Write-Host "run $Lane #$N ($Project) $(($Vars.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' ')"
    $started = [DateTime]::UtcNow
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & dotnet test "$H/probe/$Project/$Project.csproj" --no-build --artifacts-path "$H/.build/$Artifacts" `
        --logger 'trx;LogFileName=run.trx' --logger 'console;verbosity=normal' --results-directory $out *> (Join-Path $out 'console.txt')
    $exit = $LASTEXITCODE
    $clock.Stop()
    foreach ($v in $ProbeVars) { Remove-Item "Env:$v" -ErrorAction SilentlyContinue }

    $reports = "$bin/Reports"
    foreach ($f in 'Failures.md', 'Run.json') {
        if (Test-Path "$reports/$f") { Copy-Item "$reports/$f" $out }
    }
    if (Test-Path $reports) {
        Get-ChildItem $reports -Recurse -File | ForEach-Object { "{0,10}  {1}" -f $_.Length, [IO.Path]::GetRelativePath($reports, $_.FullName) } |
            Set-Content (Join-Path $out 'reports-files.txt')
    } else {
        "no Reports directory in $bin" | Set-Content (Join-Path $out 'reports-files.txt')
    }
    # Saved as .log.txt: the repository's .gitignore drops *.log, and these are evidence.
    foreach ($f in 'kronikol-error.log', 'prototype-error.log', 'prototype-formatter.log') {
        if (Test-Path "$bin/$f") { Copy-Item "$bin/$f" (Join-Path $out "$f.txt") }
    }
    if (Test-Path "$bin/starttime-probe.txt") { Copy-Item "$bin/starttime-probe.txt" $out }
    [ordered]@{
        lane = $Lane; project = $Project; run = $N; vars = $Vars; artifacts = $Artifacts
        startedUtc = $started.ToString('o'); wallSeconds = [math]::Round($clock.Elapsed.TotalSeconds, 3); exitCode = $exit
    } | ConvertTo-Json | Set-Content (Join-Path $out 'run-info.json')

    Compare-Run $out $reports "$Lane run $N"
}

# compare.py over one saved run; report-extract.json keeps the fields it reads, so -CompareOnly can redo it.
function Compare-Run([string]$Out, [string]$Reports, [string]$Label) {
    if (Test-Path (Join-Path $Out 'run.trx')) {
        & python "$H/tools/compare.py" --trx (Join-Path $Out 'run.trx') --report $Reports --json (Join-Path $Out 'compare.json') `
            --extract (Join-Path $Out 'report-extract.json') --label $Label | Set-Content (Join-Path $Out 'compare.txt')
    } else {
        'no run.trx: dotnet test wrote no results' | Set-Content (Join-Path $Out 'compare.txt')
    }
    if ((Test-Path (Join-Path $Out 'asynclocal.tsv')) -and (Test-Path (Join-Path $Out 'run.trx'))) {
        & python "$H/tools/asynclocal.py" (Join-Path $Out 'asynclocal.tsv') (Join-Path $Out 'run.trx') --json (Join-Path $Out 'asynclocal.json') |
            Set-Content (Join-Path $Out 'asynclocal.txt')
    }
}

if ($CompareOnly) {
    Get-ChildItem $Results -Directory | Where-Object { $_.Name -ne 'perf' -and (Want $_.Name) } | ForEach-Object {
        $lane = $_.Name
        Get-ChildItem $_.FullName -Directory | Where-Object { $_.Name -match '^\d+$' } | ForEach-Object {
            $extract = Join-Path $_.FullName 'report-extract.json'
            $source = if (Test-Path $extract) { $extract } else { Join-Path $_.FullName 'no-report' }
            Write-Host "compare $lane #$($_.Name)"
            Compare-Run $_.FullName $source "$lane run $($_.Name)"
        }
    }
    & python "$H/tools/summarize.py" $Results
    return
}

# --- build -----------------------------------------------------------------------------------------
foreach ($p in 'A.OwnFramework', 'A2.MethodDisplay', 'A3.StartTimeProbe', 'B.AssemblyFixtureOnly', 'C.AssemblyFixturePrototype', 'E.CollectionFixtureOnly',
    'F.DependencyInjectionPrototype', 'G.FormatterProbe') {
    Build $p
}
if (Want 'A.OwnFramework-runner3.1.5') { Build 'A.OwnFramework' 'runner3' @('-p:ProbeRunnerVersion=3.1.5') }

# --- B with both TestFramework attributes: a scratch copy, compiled only to record the error ----------
if (Want 'B.AssemblyFixtureOnly') {
    $scratch = "$H/.build/scratch/B.BothFrameworks"
    Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $scratch | Out-Null
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Xunit.Extensions.AssemblyFixture" Version="2.6.0" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="$H/probe/Shared/*.cs" />
    <Compile Include="$H/probe/AssemblyFixtureShared/*.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content "$scratch/B.BothFrameworks.csproj"
    @'
using Kronikol.xUnit2;
using Xunit;

[assembly: TestFramework("Xunit.Extensions.AssemblyFixture.AssemblyFixtureFramework", "Xunit.Extensions.AssemblyFixture")]
[assembly: TestFramework("Kronikol.xUnit2.ReportingTestFramework", "Kronikol.xUnit2")]
[assembly: TestTracking]
'@ | Set-Content "$scratch/AssemblyInfo.cs"
    New-Item -ItemType Directory -Force "$Results/B.AssemblyFixtureOnly" | Out-Null
    $log = & dotnet build "$scratch/B.BothFrameworks.csproj" --artifacts-path "$H/.build/scratch-artifacts" -c Debug 2>&1
    (@("exit code: $LASTEXITCODE", '--- AssemblyInfo.cs ---') + (Get-Content "$scratch/AssemblyInfo.cs") + @('--- errors ---') +
        ($log | Where-Object { $_ -match 'error' } | ForEach-Object { $_ -replace [regex]::Escape($H), '<harness>' } | Select-Object -Unique)) |
        Set-Content "$Results/B.AssemblyFixtureOnly/both-attributes-build.txt"
}

# --- runs (the repeated lanes interleaved, so drift on a busy machine spreads across them) -----------
for ($n = 1; $n -le $Runs; $n++) {
    Invoke-Lane 'A.OwnFramework' 'A.OwnFramework' $n
    Invoke-Lane 'C.AssemblyFixturePrototype-sync1' 'C.AssemblyFixturePrototype' $n @{ PROBE_SYNC = '1'; PROBE_FORMAT_GUARD = '1' }
    Invoke-Lane 'C.AssemblyFixturePrototype-sync0' 'C.AssemblyFixturePrototype' $n @{ PROBE_SYNC = '0'; PROBE_FORMAT_GUARD = '1' }
    Invoke-Lane 'E.CollectionFixtureOnly' 'E.CollectionFixtureOnly' $n
    Invoke-Lane 'A3.StartTimeProbe' 'A3.StartTimeProbe' $n
    Invoke-Lane 'F.DependencyInjectionPrototype-sync1' 'F.DependencyInjectionPrototype' $n @{ PROBE_SYNC = '1'; PROBE_FORMAT_GUARD = '1' }
    Invoke-Lane 'F.DependencyInjectionPrototype-sync0' 'F.DependencyInjectionPrototype' $n @{ PROBE_SYNC = '0'; PROBE_FORMAT_GUARD = '1' }
    # The formatter's exception left to escape the sink: what it breaks differs from run to run.
    Invoke-Lane 'C.AssemblyFixturePrototype-sync1-unguarded' 'C.AssemblyFixturePrototype' $n @{ PROBE_SYNC = '1' }
    Invoke-Lane 'C.AssemblyFixturePrototype-sync0-unguarded' 'C.AssemblyFixturePrototype' $n @{ PROBE_SYNC = '0' }
}
Invoke-Lane 'A.OwnFramework-runner3.1.5' 'A.OwnFramework' 1 @{} 'runner3'
Invoke-Lane 'A2.MethodDisplay' 'A2.MethodDisplay' 1
Invoke-Lane 'B.AssemblyFixtureOnly' 'B.AssemblyFixtureOnly' 1

if (Want 'G.FormatterProbe') {
    New-Item -ItemType Directory -Force "$Results/G.FormatterProbe" | Out-Null
    & dotnet "$H/.build/main/bin/G.FormatterProbe/debug/G.FormatterProbe.dll" | Set-Content "$Results/G.FormatterProbe/output.txt"
}

& python "$H/tools/summarize.py" $Results
