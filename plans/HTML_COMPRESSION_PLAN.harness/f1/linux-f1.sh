#!/bin/sh
# Step F1 on Linux WebKit (the podman runner): f1-load.js over each page given, in Playwright 1.59.1's noble image.
#   wsl -d podman-machine-default -u root -- sh linux-f1.sh /mnt/c/<path to f1> /mnt/c/<page.html> [/mnt/c/<page.html> ...]
# Each result is written beside this script as <page name>-linux-webkit.json.
set -e
here=$1; shift
rm -rf /root/probe-f1 && mkdir -p /root/probe-f1
cp "$here/f1-load.js" /root/probe-f1/
names=""
for page in "$@"; do cp "$page" /root/probe-f1/; names="$names $(basename "$page" .html)"; done
podman run --rm -v /root/probe-f1:/probe -w /probe mcr.microsoft.com/playwright:v1.59.1-noble \
  sh -c "npm i --no-save --silent playwright-core@1.59.1 >/dev/null 2>&1 && for p in $names; do node f1-load.js /probe/\$p.html webkit > /probe/\$p-linux-webkit.json 2>&1 || true; done"
cp /root/probe-f1/*-linux-webkit.json "$here/"
