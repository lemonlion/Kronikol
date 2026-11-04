#!/usr/bin/env bash
# Build settings a weave has to live with, on the published weaver and the prototype, written to results/real-build-settings.txt:
#   DebugType=full and pdbonly, which could mean a native PDB, on net10.0 and on a netstandard2.0 library;
#   Visual Studio's MSBuild, which runs the weaver, a netstandard2.0 task, on .NET Framework;
#   a DebugType changed with the old PDB still in obj.
# The prototype's pair of weavers, since a release moves them together, and in the stale PDB's case the new step weaver over the
# published assertion weaver too, which is the mix a project can hold.
#     WORK=<a folder of your own, holding feed/ with the prototype's packages> MSBUILD_EXE=<Visual Studio's MSBuild.exe> run-build-settings.sh
set -u

W="${WORK:?set WORK to a folder of your own, holding feed/ with the packages of the prototype}"
MSB="${MSBUILD_EXE:?set MSBUILD_EXE to the MSBuild.exe of Visual Studio}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="$here/../results/real-build-settings.txt"
feed="$(cygpath -m "$W/feed")"
scratch="$W/build-settings"
mkdir -p "$scratch"

config() {
    cat > "$1/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="local" value="$feed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
XML
}
fresh() {
    rm -rf "${1:?}"
    mkdir -p "$1"
    cp "$here/Program.cs" "$here/consumer.csproj" "$here/Directory.Build.props" "$here/Directory.Build.targets" "$1/"
    config "$1"
}
kind() {
    if [ ! -f "$1" ]; then echo "no pdb beside the assembly"; return; fi
    case "$(head -c 4 "$1" | od -An -c | tr -d ' ')" in
        BSJB) echo "portable pdb" ;;
        *) echo "native pdb" ;;
    esac
}

{
    for pair in "published 5.28.2 5.28.2" "prototype 5.28.99-proto226 5.28.99-proto226" "prototype-over-published-assertions 5.28.99-proto226 5.28.2"; do
        set -- $pair
        label=$1; version=$2; assertions=$3
        if [ "$label" != prototype-over-published-assertions ]; then
        for debug in full pdbonly; do
            for cfg in Debug Release; do
                p="$scratch/$label-$debug-$cfg"
                fresh "$p"
                echo "=================== $label, net10.0, DebugType=$debug, $cfg"
                (cd "$p" && dotnet build consumer.csproj -c "$cfg" -nologo -v:m -tl:off -p:DebugType="$debug" -p:StepTrackingVersion="$version" -p:AssertionTrackingVersion="$assertions" 2>&1 \
                    | grep -E " error |Build succeeded" | cut -c1-200 | sort -u)
                kind "$p/bin/$cfg/net10.0/consumer.pdb"
                "$p/bin/$cfg/net10.0/consumer" 2>&1 | grep -E "^  step |InvalidProgramException" | sed -E 's/ [0-9]+ ms,/,/' | cut -c1-110 | sort | uniq -c | sort -rn | head -5
                rm -rf "${p:?}"
            done
        done

        for debug in portable full pdbonly; do
            p="$scratch/$label-ns20-$debug"
            rm -rf "${p:?}"; mkdir -p "$p"
            echo '<Project />' > "$p/Directory.Build.props"
            echo '<Project />' > "$p/Directory.Build.targets"
            config "$p"
            cat > "$p/Lib.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <UseArtifactsOutput>false</UseArtifactsOutput>
    <DebugType>$debug</DebugType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kronikol.StepTracking" Version="$version" PrivateAssets="all" />
  </ItemGroup>
</Project>
XML
            cat > "$p/Driver.cs" <<'CS'
using System.Threading.Tasks;
using Kronikol.Tracking;

[assembly: TrackSteps]

public class Driver
{
    [Step] public void A_void() { }
    [Step] public async Task A_task() { await Task.Yield(); }
}
CS
            echo "=================== $label, netstandard2.0, DebugType=$debug"
            (cd "$p" && dotnet build Lib.csproj -c Debug -nologo -v:n -tl:off 2>&1 | grep -E " error |StepTracking: Instrumented|Build succeeded" | sed -E 's/^ +//; s/ \([0-9]+ms\)//' | cut -c1-120 | sort -u)
            kind "$p/bin/Debug/netstandard2.0/Lib.pdb"
            rm -rf "${p:?}"
        done

        p="$scratch/$label-desktop"
        fresh "$p"
        echo "=================== $label, Visual Studio's MSBuild $("$MSB" -version -nologo 2>&1 | tail -1), on .NET Framework, DebugType=portable, Debug"
        (cd "$p" && "$MSB" consumer.csproj -restore -nologo -v:m -p:Configuration=Debug -p:StepTrackingVersion="$version" -p:AssertionTrackingVersion="$assertions" 2>&1 \
            | grep -E " error |warning MSB|Build succeeded" | cut -c1-200 | sort -u)
        "$p/bin/Debug/net10.0/consumer" 2>&1 | grep -E "^references|^  step |InvalidProgramException" | sed -E 's/ [0-9]+ ms,/,/' | cut -c1-200 | head -14
        rm -rf "${p:?}"
        fi

        # A project whose DebugType changed keeps the old PDB in obj until it is cleaned, and that PDB is not the new assembly's.
        for changed in none embedded; do
        p="$scratch/$label-stale-pdb"
        fresh "$p"
        echo "=================== $label, DebugType=portable, then $changed, with no clean between"
        (cd "$p" && dotnet build consumer.csproj -c Debug -nologo -v:m -tl:off -p:StepTrackingVersion="$version" -p:AssertionTrackingVersion="$assertions" 2>&1 | grep -E " error |Build succeeded" | cut -c1-200 | sort -u)
        echo "public class Changed { }" >> "$p/Program.cs"
        (cd "$p" && dotnet build consumer.csproj -c Debug -nologo -v:m -tl:off -p:DebugType="$changed" -p:StepTrackingVersion="$version" -p:AssertionTrackingVersion="$assertions" 2>&1 | grep -E " error |warning KRONIKOL|Build succeeded" | sed -E 's/^.*(Symbols were found but are not matching).*$/error: \1 the assembly/' | cut -c1-200 | sort -u)
        "$p/bin/Debug/net10.0/consumer" 2>&1 | grep -E "^step 'Void'|^step 'Value with assertion'|in .*Program.cs:line" | sed -E 's/ [0-9]+ ms,/,/; s/ in .*Program.cs/ in Program.cs/' | cut -c1-110
        rm -rf "${p:?}"
        done

        # A TargetName of its own names the assembly too, since the compiler takes the name from the file it writes, so no weaver loses anything here. The host's files keep the project's name.
        p="$scratch/$label-target-name"
        fresh "$p"
        echo "=================== $label, TargetName=renamed"
        (cd "$p" && dotnet build consumer.csproj -c Debug -nologo -v:m -tl:off -p:TargetName=renamed -p:StepTrackingVersion="$version" -p:AssertionTrackingVersion="$assertions" 2>&1 | grep -E " error |warning KRONIKOL00[135]|Build succeeded" | cut -c1-170 | sort -u)
        (cd "$p/bin/Debug/net10.0" && dotnet exec --runtimeconfig consumer.runtimeconfig.json --depsfile consumer.deps.json renamed.dll) 2>&1 | grep -E "^step 'Void'|^step 'Value with assertion'|in .*Program.cs:line|^at ShopDriver.A_value_that_throws" | sed -E 's/ [0-9]+ ms,/,/; s/ in .*Program.cs/ in Program.cs/' | cut -c1-110
        rm -rf "${p:?}"

        # A PdbFile with a name of its own, one with no extension and one with another, which the build writes as .pdb. The assembly has to name the
        # file for the run to read it.
        for clone in a b; do
            c="$scratch/$label-clone-$clone"
            fresh "$c"
            mkdir -p "$c/symbols"
            (cd "$c" && dotnet build consumer.csproj -c Release -nologo -v:q -tl:off -p:PdbFile=symbols/custom.pdb -p:PathMap="$(cygpath -w "$c" 2>/dev/null || echo "$c")=/_/" -p:StepTrackingVersion="$version" -p:AssertionTrackingVersion="$assertions" 2>&1 | grep -E " error " | cut -c1-170 | sort -u)
        done
        echo "=================== $label, PdbFile under PathMap, two clones"
        a="$scratch/$label-clone-a/bin/Release/net10.0/consumer.dll"; b="$scratch/$label-clone-b/bin/Release/net10.0/consumer.dll"
        if cmp -s "$a" "$b"; then echo "consumer.dll: the same in both"; else echo "consumer.dll: differs"; fi
        echo "times the clone's folder is in consumer.dll: $(grep -a -c "$label-clone-a" "$a")"
        "$scratch/$label-clone-a/bin/Release/net10.0/consumer" 2>&1 | grep -E "in .*Program.cs:line" | sed -E 's/ [0-9.]+ ms//' | cut -c1-120 | sort -u | head -2
        rm -rf "${scratch:?}/$label-clone-a" "${scratch:?}/$label-clone-b"
        for named in symbols/custom.pdb symbols/custom symbols/custom.sym; do
        p="$scratch/$label-pdb-name"
        fresh "$p"
        mkdir -p "$p/symbols"
        echo "=================== $label, PdbFile=$named"
        (cd "$p" && dotnet build consumer.csproj -c Debug -nologo -v:m -tl:off -p:PdbFile="$named" -p:StepTrackingVersion="$version" -p:AssertionTrackingVersion="$assertions" 2>&1 | grep -E " error |warning KRONIKOL00[135]|Build succeeded" | cut -c1-170 | sort -u)
        ls "$p/bin/Debug/net10.0" | grep -E "\.pdb$|custom" | grep -v -E "^(Kronikol|Shouldly)" | sed 's/^/in bin: /'
        "$p/bin/Debug/net10.0/consumer" 2>&1 | grep -E "^step 'Void'|^step 'Value with assertion'|in .*Program.cs:line|^at ShopDriver.A_value_that_throws" | sed -E 's/ [0-9]+ ms,/,/; s/ in .*Program.cs/ in Program.cs/' | cut -c1-110
        rm -rf "${p:?}"
        done
    done
} | sed -E "s#[A-Za-z]:[\\\\/]Users[\\\\/][^\\\\/]+#<home>#g" > "$out"
rm -rf "${scratch:?}"
grep -c "^=" "$out" | sed 's/^/runs: /'
