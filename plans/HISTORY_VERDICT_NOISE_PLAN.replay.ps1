<#
.SYNOPSIS
    Replays a Kronikol history ledger through unmodified main and through the prototype of
    HISTORY_VERDICT_NOISE_PLAN.md, and writes what the two said as one text file of numbers.

.DESCRIPTION
    Made for a ledger that cannot leave the machine it is on. Unless -WithNames is given, the output holds
    no scenario name, feature name, scenario id, run id, suite name, branch, commit, call line or error
    text: scenarios are "s<position in the roster>", runs are "run#<position in the suite's stream>",
    suites are "suite#<position in the ledger>". What is left is verdict counts, durations, p95 bars,
    paces and call counts. Read the file before sharing it all the same.

    It builds Kronikol twice in throwaway worktrees under the temp folder (the commit the patch applies
    to, with and without the patch), runs the replay probe from each against the ledger, and removes the
    worktrees. The ledger is only read. Nothing in this repository is changed.

.EXAMPLE
    pwsh plans/HISTORY_VERDICT_NOISE_PLAN.replay.ps1 -Ledger C:\work\suite\.kronikol\history.jsonl -Last 6

.NOTES
    Needs git, the .NET SDK the repository builds with, and this repository at or after the base commit.
#>
param(
    [Parameter(Mandatory)] [string]$Ledger,
    [string]$Out = (Join-Path (Get-Location) 'noise-plan-replay.txt'),
    [int]$Last = 6,              # how many runs from the end of the suite's stream to replay, one cut each
    [int]$MinRuns = 5,           # the suite's HistoryMinRuns (Kronikol's default is 5)
    [string]$Suite = '',         # the suite to replay; default: the suite of the ledger's last run
    [switch]$WithNames,          # for your own eyes: names, ids, evidence and call lines are printed
    [string]$Base = '82abeb7f',  # the commit the prototype patch applies to (3.22.1)
    [string]$Repo = '',          # a Kronikol clone; default: the one this script is in
    [string]$Patch = '',         # default: HISTORY_VERDICT_NOISE_PLAN.prototype.patch beside this script
    [switch]$KeepWorktrees
)

$ErrorActionPreference = 'Stop'
$Ledger = (Resolve-Path -LiteralPath $Ledger).Path
$repo = if ($Repo) { (Resolve-Path -LiteralPath $Repo).Path } else { (git -C $PSScriptRoot rev-parse --show-toplevel).Trim() }
$patch = if ($Patch) { (Resolve-Path -LiteralPath $Patch).Path } else { Join-Path $PSScriptRoot 'HISTORY_VERDICT_NOISE_PLAN.prototype.patch' }
if (-not (Test-Path -LiteralPath $patch)) { throw "The prototype patch is not beside this script: $patch" }
git -C $repo cat-file -e "$Base^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) { throw "Commit $Base is not in $repo - fetch main first." }

$work = Join-Path ([IO.Path]::GetTempPath()) ('knr-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$trees = [ordered]@{ main = (Join-Path $work 'm'); proto = (Join-Path $work 'p') }
$probe = 'tests/Kronikol.Tests/History/LedgerReplayProbe.cs'
$results = @{}

try {
    foreach ($name in $trees.Keys) {
        git -C $repo worktree add --detach $trees[$name] $Base *> $null
        if ($LASTEXITCODE -ne 0) { throw "git worktree add failed for $name" }
    }
    # main gets only the half of the probe that compiles without the prototype; the prototype gets everything.
    git -C $trees.main apply --whitespace=nowarn "--include=$probe" $patch
    if ($LASTEXITCODE -ne 0) { throw 'The probe did not apply to main.' }
    git -C $trees.proto apply --whitespace=nowarn $patch
    if ($LASTEXITCODE -ne 0) { throw 'The prototype patch did not apply.' }

    foreach ($name in $trees.Keys) {
        Write-Host "building $name ..."
        $log = dotnet build (Join-Path $trees[$name] 'tests/Kronikol.Tests/Kronikol.Tests.csproj') -c Debug -nologo -v q --disable-build-servers 2>&1
        if ($LASTEXITCODE -ne 0) { $log | Select-Object -Last 30 | Write-Host; throw "The $name build failed." }
    }

    $env:KRONIKOL_HISTORY = 'off'
    $env:REPLAY_LEDGER = $Ledger
    $env:REPLAY_LAST = "$Last"
    $env:REPLAY_MINRUNS = "$MinRuns"
    $env:REPLAY_SUITE = $Suite
    $env:REPLAY_ANON = if ($WithNames) { '' } else { '1' }
    $env:REPLAY_COST = ''
    $env:REPLAY_DEGRADED_FILTER = ''
    foreach ($mode in 'verdicts', 'stats') {
        foreach ($name in $trees.Keys) {
            if ($mode -eq 'stats' -and $name -eq 'main') { continue }
            $env:REPLAY_STATS = if ($mode -eq 'stats') { '1' } else { '' }
            $env:REPLAY_ALL_SUITES = ''
            $env:REPLAY_OUT = Join-Path $work "$name.$mode.txt"
            $exe = Join-Path $trees[$name] 'tests/Kronikol.Tests/bin/Debug/net10.0/Kronikol.Tests.exe'
            & $exe -class Kronikol.Tests.History.LedgerReplayProbe -noLogo *> (Join-Path $work "$name.$mode.console.txt")
            if (-not (Test-Path -LiteralPath $env:REPLAY_OUT)) { throw "The $name probe wrote nothing ($mode): $(Get-Content (Join-Path $work "$name.$mode.console.txt") -Tail 5)" }
            $results["$name.$mode"] = [IO.File]::ReadAllLines($env:REPLAY_OUT)
        }
    }

    # What moved between main and the prototype, run by run.
    function Blocks($lines) {
        $blocks = @(); $current = $null
        foreach ($line in $lines) {
            if ($line.StartsWith('=== ')) { $current = @{ head = $line; counts = ''; bars = @() }; $blocks += $current; continue }
            if ($null -eq $current) { continue }
            if ($line.StartsWith('counts: ')) { $current.counts = $line.Substring(8) }
            elseif ($line.StartsWith('bars: ')) { $current.bars = $line.Substring(6) -split ' ' }
        }
        , $blocks
    }
    $m = Blocks $results['main.verdicts']; $p = Blocks $results['proto.verdicts']
    $compare = @()
    for ($i = 0; $i -lt $p.Count; $i++) {
        $moved = 0; $largest = 1.0; $both = 0
        for ($s = 0; $s -lt [Math]::Min($m[$i].bars.Count, $p[$i].bars.Count); $s++) {
            $a = ($m[$i].bars[$s] -split '/')[1]; $b = ($p[$i].bars[$s] -split '/')[1]
            if ($a -eq '-' -or $b -eq '-') { continue }
            $both++
            if ($a -ne $b) {
                $moved++
                $ratio = if ([double]$b -gt 0) { [double]$a / [double]$b } else { 0 }
                if ($ratio -gt $largest) { $largest = $ratio }
            }
        }
        $compare += $p[$i].head
        $compare += '    main : ' + $m[$i].counts
        $compare += '    proto: ' + $p[$i].counts
        $compare += ('    p95 bars: {0} of {1} differ; main''s bar is at most {2:0.00}x the prototype''s' -f $moved, $both, $largest)
    }

    $text = @()
    $text += "Kronikol history ledger replay - HISTORY_VERDICT_NOISE_PLAN.md (base $Base, names $(if ($WithNames) { 'PRINTED' } else { 'withheld' }), MinRuns=$MinRuns, last $Last runs)"
    $text += ''
    $text += '## main against the prototype, run by run'
    $text += $compare
    $text += ''
    $text += '## what unmodified main says'
    $text += $results['main.verdicts']
    $text += ''
    $text += '## what the prototype says'
    $text += $results['proto.verdicts']
    $text += ''
    $text += '## every full run of every suite: the statistics a pace could be made of (pace = p50f10)'
    $text += $results['proto.stats']
    [IO.File]::WriteAllLines($Out, $text)
    Write-Host "written: $Out"
}
finally {
    if (-not $KeepWorktrees) {
        foreach ($name in $trees.Keys) { git -C $repo worktree remove --force $trees[$name] *> $null }
        git -C $repo worktree prune *> $null
        if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
