# S14's component box colours, undone a box at a time: each mutation puts one box of the component diagram's or its
# diff's skin back on its 4.x colour, and must fail both the core theory (DiagramLookTests) and the Playwright theory
# (DiagramActivityAndComponentColoursTests). The file is put back after each run.
# usage: pwsh mutate-s14-contrast.ps1 <worktree> <out dir>
param([string]$W, [string]$O)
$gen = 'src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs'
$diff = 'src/Kronikol/ComponentDiagram/ComponentDiagramDiffer.cs'
$core = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
$e2e = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
$coreTheory = 'FullyQualifiedName~.DiagramLookTests.Every_box'
$pwTheory = 'FullyQualifiedName~Kronikol.Tests.EndToEnd.DiagramActivityAndComponentColoursTests.Every_component_box'
$mutations = @(
    @('service-box-c4-blue', $gen, '  BackgroundColor #3170BA', '  BackgroundColor #438DD5'),
    @('diff-service-box-c4-blue', $diff, '  BackgroundColor #3170BA', '  BackgroundColor #438DD5'),
    @('diff-new-limegreen', $diff, '  BackgroundColor #2E7D32', '  BackgroundColor #LimeGreen'),
    @('diff-removed-red', $diff, '  BackgroundColor #C62828', '  BackgroundColor #Red')
)
New-Item -ItemType Directory -Force $O | Out-Null
Push-Location $W
try {
    foreach ($m in $mutations) {
        $file = "$W/$($m[1])"
        $bytes = [IO.File]::ReadAllBytes($file)
        $fixed = [IO.File]::ReadAllText($file)
        if (-not $fixed.Contains($m[2])) { "$($m[0]) NOT APPLIED"; continue }
        try {
            [IO.File]::WriteAllText($file, $fixed.Replace($m[2], $m[3]))
            foreach ($run in @(@('core', $core, $coreTheory), @('pw', $e2e, $pwTheory))) {
                & dotnet test $run[1] -nodeReuse:false -p:UseSharedCompilation=false --filter $run[2] *> "$O/$($m[0])-$($run[0]).log"
                if ($LASTEXITCODE -ne 0) { "$($m[0]) $($run[0]) killed" } else { "$($m[0]) $($run[0]) SURVIVED" }
            }
        } finally {
            [IO.File]::WriteAllBytes($file, $bytes)
            (Get-Item $file).LastWriteTime = Get-Date
        }
    }
} finally {
    Pop-Location
}
