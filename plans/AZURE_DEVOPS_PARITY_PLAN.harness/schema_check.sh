#!/usr/bin/env bash
# Plan §6's schema fact, run on what the plan drafts today: every YAML block of the plan's Appendix A, and
# s0-probe.yml, validated against Microsoft's published Azure Pipelines schema (service-schema.json from
# microsoft/azure-pipelines-vscode, draft-07). Two things the check needs that §6 first missed, both shown
# here: the schema types every scalar as a string (a boolean is a string matching ^true$ and its
# spellings), so the YAML is read with no type resolution, as the pipeline parser reads it; and the schema
# knows each task input by its canonical name only, so an alias the service accepts (DownloadPipelineArtifact's
# `path` for `targetPath`) fails the check. A deliberately wrong file must fail it, or the check proves
# nothing.
#
# Usage: schema_check.sh   (from the repository root; python3 and pip, and network access to
#        raw.githubusercontent.com and PyPI)
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(git -C "$here" rev-parse --show-toplevel)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

curl -sSL -o "$work/service-schema.json" https://raw.githubusercontent.com/microsoft/azure-pipelines-vscode/main/service-schema.json
python3 -m pip install --quiet --disable-pip-version-check --target "$work/py" jsonschema pyyaml > /dev/null 2>&1
echo "schema: $(python3 -c 'import json,sys; s=json.load(open(sys.argv[1])); print(s["$schema"], s.get("$comment"))' "$work/service-schema.json")," \
  "$(wc -c < "$work/service-schema.json" | tr -d ' ') bytes, $(gzip -9c "$work/service-schema.json" | wc -c | tr -d ' ') gzipped," \
  "sha256 $(sha256sum "$work/service-schema.json" | cut -c1-16)…"

# Appendix A's YAML blocks, one file each.
awk -v dir="$work" '/^## Appendix A/ { a = 1 } /^## Appendix B/ { a = 0 }
     a && /^```yaml/ { n++; f = sprintf("%s/appendix-a-%d.yml", dir, n); body = 1; next }
     body && /^```/ { body = 0; close(f); next }
     body { print > f }' "$root/plans/AZURE_DEVOPS_PARITY_PLAN.md"
cp "$here/s0-probe.yml" "$work/s0-probe.yml"
# Two files that must fail: a misspelt key, and the input alias the first draft used.
printf '%s\n' 'steps:' '- checkout: self' '  persistCredential: true' > "$work/must-fail-misspelt-key.yml"
printf '%s\n' 'steps:' '- task: DownloadPipelineArtifact@2' '  inputs:' '    path: $(Agent.TempDirectory)/fragments' \
  > "$work/must-fail-input-alias.yml"

PYTHONPATH="$work/py" python3 - "$work/service-schema.json" "$work"/appendix-a-*.yml "$work/s0-probe.yml" "$work"/must-fail-*.yml <<'PY'
import json, sys
import jsonschema, yaml

class Text(yaml.SafeLoader):
    """Every scalar stays the text it was written as: the pipeline parser resolves no ints, floats or bools."""
Text.yaml_implicit_resolvers = {}

validator = jsonschema.Draft7Validator(json.load(open(sys.argv[1])))
for path in sys.argv[2:]:
    errors = list(validator.iter_errors(yaml.load(open(path), Loader=Text)))
    print(f"{path.rsplit('/', 1)[-1]}: {'valid' if not errors else 'INVALID'}")
    for error in errors:
        # Of a oneOf/anyOf failure, the branch that got deepest is the one the author meant; among equals,
        # not the branches that only failed to match the task's name.
        while error.context:
            error = max(error.context, key=lambda e: (len(e.absolute_path), e.validator != "pattern"))
        where = "/".join(str(p) for p in error.absolute_path)
        print(f"    at {where or '(root)'}: {error.message[:150]}")
PY
