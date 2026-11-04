#!/bin/sh
# The real-build consumers on Linux, in the .NET container of the podman-machine-default WSL distro: a project built from
# packages and run as a program under each DebugType, and whether a reused MSBuild node holds a referenced assembly (F19).
#   wsl -d podman-machine-default -u root -- sh linux-consumers.sh <harness under /mnt/c> <feed under /mnt/c> <results dir under /mnt/c>
# Writes <results dir>/linux-consumers.txt. The feed holds the prototype's two weavers as 5.28.99-proto226.
harness="$1"; feed="$2"; out="$3"
work=/root/k226-consumers
rm -rf "$work"; mkdir -p "$work/h" "$work/feed" "$work/out" "$out"
cp -r "$harness/consumer" "$harness/consumer-lock" "$work/h/"
cp "$feed"/*.nupkg "$work/feed/"
# A pack of the same version as the last round's is not looked at if the last one is still in the cache.
rm -rf /root/nuget/kronikol.steptracking/5.28.99-proto226 /root/nuget/kronikol.assertiontracking/5.28.99-proto226

cat > "$work/inner.sh" <<'INNER'
#!/bin/bash
set -u
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
H=/w/h
C=$H/consumer

config_nuget() {
    cat > "$1/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="local" value="/w/feed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
XML
}

echo "sdk $(dotnet --version), $(uname -sr)"
for pair in "published 5.28.2" "prototype 5.28.99-proto226"; do
    set -- $pair
    label=$1; version=$2
    p=/tmp/c/$label-case
    rm -rf "$p"; mkdir -p "$p/symbols"
    cp "$C/Program.cs" "$C/consumer.csproj" "$C/Directory.Build.props" "$C/Directory.Build.targets" "$p/"
    config_nuget "$p"
    echo "=================== $label, AssemblyName=App, PdbFile=symbols/app.pdb"
    (cd "$p" && dotnet build consumer.csproj -c Release -v:m -tl:off -p:AssemblyName=App -p:PdbFile=symbols/app.pdb -p:StepTrackingVersion="$version" -p:AssertionTrackingVersion="$version" 2>&1 \
        | grep -E " error |Build succeeded" | sed -E 's#^/[^ ]*/##; s#/tmp/c/[^ ]*/##g' | cut -c1-200 | sort -u)
    "$p/bin/Release/net10.0/App" 2>&1 | grep -E "at ShopDriver.A_value_that_throws" | cut -c1-120 | sort -u | head -1
    rm -rf "$p"
    for debug in portable embedded none; do
        for cfg in Debug Release; do
            p=/tmp/c/$label-$debug-$cfg
            rm -rf "$p"; mkdir -p "$p"
            cp "$C/Program.cs" "$C/consumer.csproj" "$C/Directory.Build.props" "$C/Directory.Build.targets" "$p/"
            config_nuget "$p"
            echo "=================== $label, DebugType=$debug, $cfg"
            (cd "$p" && dotnet build consumer.csproj -c "$cfg" -v:m -tl:off -p:DebugType="$debug" -p:StepTrackingVersion="$version" -p:AssertionTrackingVersion="$version" 2>&1 \
                | grep -E " error |warning KRONIKOL|Build succeeded" | sed -E 's#^/[^ ]*/##; s#/tmp/c/[^ ]*/##g' | cut -c1-200 | sort -u)
            "$p/bin/$cfg/net10.0/consumer" 2>&1 | grep -vE "^ +at |^references|^runtime" | cut -c1-160
            echo "exit ${PIPESTATUS[0]}"
            "$p/bin/$cfg/net10.0/consumer" ref 2>&1 | grep -vE "^ +at |^references|^runtime" | cut -c1-160 | head -10
            echo "exit of the ref run: ${PIPESTATUS[0]}"
            rm -rf "$p"
        done
    done
done

# F19: three projects whose steps return a type from a project they share, built, edited and built again on reused nodes, with
# nothing collecting and no build in the entry process, as consumer-lock/run-lock.sh does on Windows.
sed -e 's/cygpath -u "$TEMP" 2>\/dev\/null || echo "${TMPDIR:-\/tmp}"/echo \/tmp/' "$H/consumer-lock/run-lock.sh" > /tmp/run-lock.sh
for pair in "published 5.28.2" "prototype 5.28.99-proto226"; do
    set -- $pair
    echo "=================== lock, $1"
    mkdir -p /tmp/h/consumer /tmp/h/consumer-lock /tmp/h/results
    cp "$C/Directory.Build.props" "$C/Directory.Build.targets" /tmp/h/consumer/
    config_nuget /tmp/h/consumer
    cp /tmp/run-lock.sh /tmp/h/consumer-lock/run-lock.sh
    bash /tmp/h/consumer-lock/run-lock.sh "linux-$1" "$2" 2>&1 | cut -c1-200
done
INNER

podman run --rm -v "$work:/w" -v /root/nuget:/root/.nuget/packages mcr.microsoft.com/dotnet/sdk:10.0 bash /w/inner.sh > "$work/out/linux-consumers.txt" 2>&1
echo "exit $?"
cp "$work/out/linux-consumers.txt" "$out/linux-consumers.txt" && rm -rf "$work"
