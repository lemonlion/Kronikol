#!/usr/bin/env bash
# Usage: run-all.sh <results label> <Kronikol version> [suite ...]
# Every suite in turn against one version of the packages. Control: run-all.sh control 5.28.2. Variant: run-all.sh variant 5.28.3-shapes3.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
LABEL="$1"
VERSION="$2"
shift 2
SUITES="${*:-nunit4 mstest tunit xunit2 xunit3 reqnroll-xunit2 reqnroll-xunit3 reqnroll-nunit reqnroll-tunit reqnroll-server bddfy-xunit3 xunit3-plain lightbdd-xunit2}"
for suite in $SUITES; do
    bash "$HERE/run-suite.sh" "$suite" "$LABEL" "$VERSION"
done
echo "done $LABEL"
