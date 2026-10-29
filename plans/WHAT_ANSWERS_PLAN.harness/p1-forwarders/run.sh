#!/usr/bin/env bash
# P1: runs forwarders.cs against the released Kronikol it names, then `query parties` on the report it wrote, with the
# query.cs the run put beside that report (no tool install). usage: run.sh <reports dir> > results-<version>.txt
set -eu
reports=$1
here=$(cd "$(dirname "$0")" && pwd)
(cd "$here" && dotnet run --file forwarders.cs -- "$reports")
echo
echo "\$ query parties"
(cd "$reports" && dotnet run --file query.cs -- parties "$reports")
