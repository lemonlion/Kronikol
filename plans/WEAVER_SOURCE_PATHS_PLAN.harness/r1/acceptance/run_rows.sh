#!/usr/bin/env bash
# WEAVER_SOURCE_PATHS_PLAN section 5.4: the probe's rows a to k again, on the locally packed 4.20.3-acceptance.1
# packages (R1). Each row is run_row.sh's clean build and test run.
set -u
A=<TEMP>/claude/c--Code-Kronikol/1a468123-a7e8-45cc-b875-791076e4d49a/scratchpad/accept165
mkdir -p "$A/logs"
P=$A/repo/IssueStack
PW=$(cygpath -w "$P")
CI=-p:ContinuousIntegrationBuild=true
row() { bash "$A/run_row.sh" "$@" > /dev/null 2>&1; echo "row $1 done: $(grep -E 'dotnet test exit|diagram notes' "$A/logs/$1-summary.txt" | tr '\n' ' ')"; }
row a "$P" Debug
row b "$P" Debug $CI
row c "$P" Debug $CI -p:DeterministicSourcePaths=false
row d "$P" Debug $CI -p:EmbedAllSources=true
row e "$P" Debug $CI -p:DebugType=embedded
row f "$P" Debug -p:DeterministicSourcePaths=true
row g "$P" Debug "-p:PathMap=$PW\\=/src/"
row h "$A/nogit/IssueStack" Debug $CI
row i "$P" Release $CI
row j "$P" Debug $CI -p:IncludeLinked=true
row k "$A/repo-k/IssueStack" Debug $CI
echo all rows done
