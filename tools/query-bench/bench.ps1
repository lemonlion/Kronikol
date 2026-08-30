# CLI wall-clock bench for QUERY_PERF_PLAN.md: same-session medians of 3 (after 1 warmup), Release.
# Numbers are only comparable within one session on one machine - see README.md for the protocol.
#
#   dotnet build src/Kronikol.Tool -c Release
#   pwsh tools/query-bench/bench.ps1 [-Report <path>] [-Reps 3]

param(
    [string]$Report = (Join-Path $PSScriptRoot "TestRunReport.query-bench.json"),
    [string]$Tool = (Join-Path $PSScriptRoot "..\..\src\Kronikol.Tool\bin\Release\net10.0\Kronikol.Tool.exe"),
    [int]$Reps = 3
)

if (-not (Test-Path $Report)) {
    Write-Error "No report at $Report - generate one: dotnet run -c Release --project tools/query-bench/gen -- $Report"
    exit 2
}
if (-not (Test-Path $Tool)) {
    Write-Error "No tool at $Tool - build it: dotnet build src/Kronikol.Tool -c Release"
    exit 2
}

$commands = @(
    @{ Name = "summary";              Args = @("query", "summary", $Report) },
    @{ Name = "values `$.status";     Args = @("query", "values", $Report, "--path", '$.status') },
    @{ Name = "grep 4173 --number";   Args = @("query", "grep", $Report, "4173", "--number", "--count") }
)

Write-Host ("{0,-22} {1,8}   {2}" -f "command", "median", "reps")
foreach ($command in $commands) {
    # Warmup rep (page cache) then the measured ones.
    & $Tool @($command.Args) *> $null
    if ($LASTEXITCODE -ne 0) { Write-Error "$($command.Name) exited $LASTEXITCODE"; exit 1 }

    $times = @()
    for ($i = 0; $i -lt $Reps; $i++) {
        $elapsed = Measure-Command { & $Tool @($command.Args) *> $null }
        $times += $elapsed.TotalSeconds
    }
    $median = ($times | Sort-Object)[[math]::Floor($times.Count / 2)]
    $all = ($times | ForEach-Object { "{0:F2}" -f $_ }) -join " "
    Write-Host ("{0,-22} {1,7:F2}s   [{2}]" -f $command.Name, $median, $all)
}
