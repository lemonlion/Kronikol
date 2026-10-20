# S14's label column, undone a rule at a time: each mutation of stylesheets.css must fail StylesheetRulesTests' fact,
# and the first (the label rule's width) also the Firefox fact in FilterBarTests (the two autos sit where its 1,400 px
# does not reach). The sheet is put back byte for byte after each run.
# usage: pwsh mutate-s14-label.ps1 <worktree> <out dir>
param([string]$W, [string]$O)
$css = "$W/src/Kronikol/Reports/stylesheets.css"
$core = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
$e2e = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
$coreFact = 'FullyQualifiedName~.StylesheetRulesTests.The_filter_rows_label_column'
$ffFact = 'FullyQualifiedName~Kronikol.Tests.EndToEnd.FilterBarTests.In_Firefox_the_Status_group'
$mutations = @(
    @('label-width-gone', 'flex: 0 0 92px; width: 92px;', 'flex: 0 0 92px;', $true),
    @('inline-label-auto-gone', '{ flex-basis: auto; width: auto; }', '{ flex-basis: auto; }', $false),
    @('narrow-label-auto-gone', '{ flex-basis: 100%; width: auto; margin', '{ flex-basis: 100%; margin', $false)
)
New-Item -ItemType Directory -Force $O | Out-Null
$bytes = [IO.File]::ReadAllBytes($css)
$fixed = [IO.File]::ReadAllText($css)
Push-Location $W
try {
    foreach ($m in $mutations) {
        if (-not $fixed.Contains($m[1])) { "$($m[0]) NOT APPLIED"; continue }
        try {
            [IO.File]::WriteAllText($css, $fixed.Replace($m[1], $m[2]))
            $runs = @(@('core', $core, $coreFact))
            if ($m[3]) { $runs += , @('firefox', $e2e, $ffFact) }
            foreach ($run in $runs) {
                & dotnet test $run[1] -nodeReuse:false -p:UseSharedCompilation=false --filter $run[2] *> "$O/$($m[0])-$($run[0]).log"
                if ($LASTEXITCODE -ne 0) { "$($m[0]) $($run[0]) killed" } else { "$($m[0]) $($run[0]) SURVIVED" }
            }
        } finally {
            [IO.File]::WriteAllBytes($css, $bytes)
            (Get-Item $css).LastWriteTime = Get-Date
        }
    }
} finally {
    Pop-Location
}
