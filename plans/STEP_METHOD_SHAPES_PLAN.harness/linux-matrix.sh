#!/bin/sh
# The shape probe on Linux, from the Windows-built binaries, in the .NET container of the podman-machine-default WSL distro:
#   wsl -d podman-machine-default -u root -- sh linux-matrix.sh <label> <binaries under /mnt/c> <results dir under /mnt/c> <STRICT_SENTINEL: 0 or 1>
label="$1"; bin="$2"; out="$3"
mkdir -p "$out"
rm -rf "/root/k226-$label"; mkdir -p "/root/k226-$label"; cp -r "$bin/." "/root/k226-$label/"
mkdir -p "/root/k226-$label-out"
podman run --rm -v "/root/k226-$label:/probe:ro" -v "/root/k226-$label-out:/out" -e STRICT_SENTINEL="$4" mcr.microsoft.com/dotnet/sdk:10.0 dotnet /probe/probe.dll all "$label" /out > "/root/k226-$label.log" 2>&1
echo "exit $?"
cp -r "/root/k226-$label-out/." "$out/"
rm -rf "/root/k226-$label-out"
rm -rf "/root/k226-$label"
