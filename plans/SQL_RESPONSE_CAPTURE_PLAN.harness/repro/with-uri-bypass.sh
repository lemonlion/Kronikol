#!/bin/sh
# Runs the repro with a temporary bypass of the SqlTrackingInterceptor URI bug (LocalDB data source), then reverts.
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"; F="$ROOT/src/Kronikol.Extensions.EfCore.Relational/SqlTrackingInterceptor.cs"
sed -i "s|        var dataSource = (command.Connection?.DataSource ?? \"localhost\").Replace(',', ':');|&\n        if (Uri.CheckHostName(dataSource.Split(':')[0]) == UriHostNameType.Unknown) dataSource = \"probe-host\"; // TEMP PROBE PATCH|" $F
cd "$(dirname "$0")" && dotnet build -c Release -nologo -v q 2>&1 | grep -E " error |Build succeeded"
dotnet run -c Release --no-build -- "$@"
git -C "$ROOT" checkout -- src/Kronikol.Extensions.EfCore.Relational/SqlTrackingInterceptor.cs
echo "worktree changes: $(git -C "$ROOT" status --short | wc -l)"
