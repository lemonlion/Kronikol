# S14's empty state on a page without the bar, undone a piece at a time: each mutation of report-filter-bar-script.js
# must fail one of FilterBarTests' three empty-state facts (typed with the bar, typed without it, a link's filters with
# and without it). The script is put back byte for byte after each run.
# usage: pwsh mutate-s14-empty.ps1 <worktree> <out dir>
param([string]$W, [string]$O)
$js = "$W/src/Kronikol/Reports/report-filter-bar-script.js"
$e2e = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
$facts = (@('No_scenario_left', 'On_a_page_without_the_bar', 'A_link_whose_filters') |
    ForEach-Object { "FullyQualifiedName~Kronikol.Tests.EndToEnd.FilterBarTests.$_" }) -join '|'
$mutations = @(
    @('listener-no-empty', "function () { emptyState(); hooks.sync(); hooks.paint(); }", "function () { hooks.sync(); hooks.paint(); }"),
    @('start-no-empty', "        // a link's filters were read before this started`n        emptyState();`n", ""),
    @('clear-only-with-bar', "        if (empty) `$('.filters-empty-clear', empty)", "        if (empty && nav) `$('.filters-empty-clear', empty)"),
    @('never-hides', "empty.hidden = !(readout.classList.contains('is-filtered') && m && m[1] === '0');", "if (readout.classList.contains('is-filtered') && m && m[1] === '0') empty.hidden = false;")
)
New-Item -ItemType Directory -Force $O | Out-Null
$bytes = [IO.File]::ReadAllBytes($js)
$fixed = [IO.File]::ReadAllText($js)
Push-Location $W
try {
    foreach ($m in $mutations) {
        if (-not $fixed.Contains($m[1])) { "$($m[0]) NOT APPLIED"; continue }
        try {
            [IO.File]::WriteAllText($js, $fixed.Replace($m[1], $m[2]))
            (Get-Item $js).LastWriteTime = Get-Date
            & dotnet test $e2e -nodeReuse:false -p:UseSharedCompilation=false --filter $facts *> "$O/$($m[0]).log"
            if ($LASTEXITCODE -ne 0) { "$($m[0]) killed" } else { "$($m[0]) SURVIVED" }
        } finally {
            [IO.File]::WriteAllBytes($js, $bytes)
            (Get-Item $js).LastWriteTime = Get-Date
        }
    }
} finally {
    Pop-Location
}
