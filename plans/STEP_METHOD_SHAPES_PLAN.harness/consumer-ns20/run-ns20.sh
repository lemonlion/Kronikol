#!/usr/bin/env bash
# A netstandard2.0 driver library with [Step] methods, whose ValueTask comes from the System.Threading.Tasks.Extensions
# package and not from the core library, run from a net10.0 program. The weaver's own framework has no ValueTask, so it names
# one from the method's own reference to it; this is the shape that shows it does.
#   run-ns20.sh <label> <Kronikol.StepTracking version> [local feed]
set -u

label="$1"
version="$2"
feed="${3:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-consumer/ns20/$label"
rm -rf "${work:?}"
mkdir -p "$work/Lib" "$work/App" "$results"

cat > "$work/Directory.Build.props" <<'XML'
<Project />
XML
cat > "$work/Directory.Build.targets" <<'XML'
<Project />
XML
sed -e 's/\r$//' "$here/../consumer/nuget.config" > "$work/nuget.config"
if [ -n "$feed" ]; then
    sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$work/nuget.config"
fi

cat > "$work/Lib/Lib.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <UseArtifactsOutput>false</UseArtifactsOutput>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kronikol.StepTracking" Version="$version" PrivateAssets="all" />
    <PackageReference Include="System.Threading.Tasks.Extensions" Version="4.6.3" />
  </ItemGroup>
</Project>
XML
cat > "$work/Lib/Driver.cs" <<'CS'
using System.Threading;
using System.Threading.Tasks;
using Kronikol.Tracking;

[assembly: TrackSteps]

public class Driver
{
    [Step] public async ValueTask A_value_task() { await Task.Delay(100); Inner(); }
    [Step] public async ValueTask<int> A_value_task_of_int() { await Task.Delay(100); Inner(); return 7; }
    [Step] public int A_value() { Thread.Sleep(20); Inner(); return 3; }
    [Step] public void Inner() { }
}
CS
cat > "$work/App/App.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseArtifactsOutput>false</UseArtifactsOutput>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kronikol" Version="5.28.2" />
    <ProjectReference Include="../Lib/Lib.csproj" />
  </ItemGroup>
</Project>
XML
cat > "$work/App/Program.cs" <<'CS'
using Kronikol.Tracking;

await Run("async ValueTask", async () => await new Driver().A_value_task());
await Run("async ValueTask<int>", async () => Console.WriteLine("  value " + await new Driver().A_value_task_of_int()));
await Run("int", async () => Console.WriteLine("  value " + new Driver().A_value()));

static async Task Run(string name, Func<Task> body)
{
    var id = Guid.NewGuid().ToString("N");
    using var scope = TestIdentityScope.Begin(id, id);
    Console.WriteLine(name);
    try { await body(); }
    catch (Exception ex) { Console.WriteLine($"  threw {ex.GetType().Name}: {ex.Message}"); }
    foreach (var step in StepCollector.GetSteps(id))
        Console.WriteLine($"  step '{step.Text}' {step.Status} {step.Duration?.TotalMilliseconds:F0} ms, under it: {string.Join(", ", (step.SubSteps ?? []).Select(s => s.Text))}");
    if (StepCollector.HasActiveStep(id))
        Console.WriteLine("  a step was left open");
}
CS

{
    echo "$label: a netstandard2.0 library's [Step] methods, run from net10.0"
    (cd "$work/App" && dotnet build App.csproj -nologo -v:m -tl:off 2>&1 | grep -E " error |warning KRONIKOL|Build succeeded" | python "$here/../consumer/scrub.py" | cut -c1-220 | sort -u)
    "$work/App/bin/Debug/net10.0/App" 2>&1 | cut -c1-200
} | tee "$results/ns20-$label.txt"
rm -rf "${work:?}/App/bin" "${work:?}/App/obj" "${work:?}/Lib/bin" "${work:?}/Lib/obj"
