#!/bin/sh
# The probe in Playwright's Linux builds, on this machine's podman runner (see the memory note on the podman WSL
# distro): wsl -d podman-machine-default -u root -- sh linux-webkit.sh /mnt/c/<path to this folder>
set -e
rm -rf /root/probe-wk && mkdir -p /root/probe-wk
cp "$1/probe.html" "$1/run-playwright.js" /root/probe-wk/
podman run --rm -v /root/probe-wk:/probe -w /probe mcr.microsoft.com/playwright:v1.59.1-noble \
  sh -c "npm i --no-save --silent playwright-core@1.59.1 >/dev/null 2>&1 && node run-playwright.js webkit,chromium,firefox"
