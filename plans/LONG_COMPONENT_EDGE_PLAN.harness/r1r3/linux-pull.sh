set -e
podman pull mcr.microsoft.com/playwright:v1.59.1-noble 2>&1 | tail -2
podman run --rm mcr.microsoft.com/playwright:v1.59.1-noble sh -c 'ls /ms-playwright; node --version; uname -a; ulimit -s'
