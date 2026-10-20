# S14's theme fix, undone a piece at a time: each mutation of report-theme-switch-script.js must fail ThemeTests (the
# first two the storage-refused theory, the third the class, where the fact that a stored pick holds over the system
# fails too). The script is put back after each run. usage: pwsh mutate-s14-theme.ps1 <worktree> <out dir>
param([string]$W, [string]$O)
$script = "$W/src/Kronikol/Reports/report-theme-switch-script.js"
$e2e = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
$theory = 'FullyQualifiedName~Kronikol.Tests.EndToEnd.ThemeTests.Where_storage_is_refused'
$class = 'FullyQualifiedName~Kronikol.Tests.EndToEnd.ThemeTests'
$fixed = [IO.File]::ReadAllText($script)
$mutations = @(
    @('pick-not-kept', ('            picked = t;' + "`n"), '', $theory),
    @('pick-not-read', ('        if (picked) return picked;' + "`n"), '', $theory),
    @('follow-overrules-a-pick', 'if (!stored()) set(e.matches', 'set(e.matches', $class)
)
New-Item -ItemType Directory -Force $O | Out-Null
Push-Location $W
try {
    foreach ($m in $mutations) {
        if (-not $fixed.Contains($m[1])) { "$($m[0]) NOT APPLIED"; continue }
        [IO.File]::WriteAllText($script, $fixed.Replace($m[1], $m[2]))
        & dotnet test $e2e -nodeReuse:false -p:UseSharedCompilation=false --filter $m[3] *> "$O/$($m[0]).log"
        if ($LASTEXITCODE -ne 0) { "$($m[0]) killed" } else { "$($m[0]) SURVIVED" }
        [IO.File]::WriteAllText($script, $fixed)
    }
} finally {
    [IO.File]::WriteAllText($script, $fixed)
    (Get-Item $script).LastWriteTime = Get-Date
    Pop-Location
}
