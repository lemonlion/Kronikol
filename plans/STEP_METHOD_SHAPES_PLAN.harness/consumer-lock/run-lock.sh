#!/usr/bin/env bash
# Three projects whose [Step] methods return a type from a project they all reference, built, edited and built again with plain
# dotnet build on reused MSBuild nodes, as a developer does. Asking what a step returns opens the assembly that holds the type:
# left open on a node, it stops the next build copying that assembly (MSB3027). A race: read the plan's F19 before trusting a clean run.
#   run-lock.sh <label> <Kronikol.StepTracking version> [local feed] [extra PackageReference for the apps]
set -u

label="$1"
version="$2"
feed="${3:-}"
extra="${4:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-consumer/lock/$label"
rm -rf "${work:?}"
mkdir -p "$results" "$work/Dep"

cp "$here/../consumer/Directory.Build.props" "$here/../consumer/Directory.Build.targets" "$work/"
sed -e 's/\r$//' "$here/../consumer/nuget.config" > "$work/nuget.config"
if [ -n "$feed" ]; then
    sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$work/nuget.config"
fi

cat > "$work/Dep/Dep.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ProduceReferenceAssembly>true</ProduceReferenceAssembly>
    <UseArtifactsOutput>false</UseArtifactsOutput>
  </PropertyGroup>
</Project>
XML

for app in App1 App2 App3; do
    mkdir -p "$work/$app"
    cat > "$work/$app/$app.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <UseArtifactsOutput>false</UseArtifactsOutput>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Dep/Dep.csproj" />
    <PackageReference Include="Kronikol" Version="5.28.2" />
    <PackageReference Include="Kronikol.StepTracking" Version="$version" PrivateAssets="all" />
    $extra
  </ItemGroup>
</Project>
XML
    cat > "$work/$app/Steps.cs" <<'CS'
using Kronikol.Tracking;

[assembly: TrackSteps]

public class Steps
{
    [Step]
    public Customer A_customer_is_registered() => new Customer();
}
CS
    # With the assertion weaver in the build too, reading an assertion about the same type.
    if [ -n "$extra" ]; then
        cat > "$work/$app/Checks.cs" <<'CS'
using Shouldly;
using Kronikol.Tracking;

[assembly: TrackAssertions]

public class Checks
{
    public void A_customer_is_there()
    {
        var customer = new Customer();
        customer.ShouldNotBeNull();
        customer.ShouldBe(customer);
    }
}
CS
    fi
done

cat > "$work/all.proj" <<'XML'
<Project DefaultTargets="Build">
  <Target Name="Restore"><MSBuild Projects="App1/App1.csproj;App2/App2.csproj;App3/App3.csproj" Targets="Restore" /></Target>
  <Target Name="Build"><MSBuild Projects="App1/App1.csproj;App2/App2.csproj;App3/App3.csproj" Targets="Build" BuildInParallel="true" /></Target>
</Project>
XML

# Nodes of their own, so nothing else's build is touched, which leave by themselves two minutes after the last build.
export MSBUILDNODEHANDSHAKESALT="kronikol-226-lock-$label" MSBUILDNODECONNECTIONTIMEOUT=120000
unset MSBUILDDISABLENODEREUSE
# A file the weave leaves open closes when a collection finalizes its handle, which a busy node may reach before the next build.
# A large first generation keeps the nodes from collecting, so the answer does not depend on that race.
export DOTNET_GCgen0size=0x20000000
# And no build runs in the entry process, whose handles die with it: every project lands on a node that stays, as most do on a busy build.
export MSBUILDNOINPROCNODE=1

{
    echo "$label: Kronikol.StepTracking $version ${extra:+with $extra}"
    for i in 0 1 2 3; do
        echo "public class Customer { public int V$i; }" > "$work/Dep/Customer.cs"
        log="$work/build-$i.log"
        (cd "$work" && dotnet build all.proj -nologo -m:3 -p:UseSharedCompilation=false > "$log" 2>&1)
        code=$?
        echo "build $i exit $code: $(grep -oE '(warning|error) (MSB30[0-9]+|KRONIKOL00[0-9])' "$log" | sort | uniq -c | tr -s ' ' | tr '\n' ';')"
    done
} | tee "$results/lock-$label.txt"
rm -rf "${work:?}"
