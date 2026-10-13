#!/bin/sh
# Writes probe-local.cs: probe.cs against the worktree's ProxyTap project (the prototype) instead of the package.
cd "$(dirname "$0")"
sed 's|^#:package Kronikol.Extensions.ProxyTap@.*|#:project ../../../src/Kronikol.Extensions.ProxyTap/Kronikol.Extensions.ProxyTap.csproj|' probe.cs > probe-local.cs
