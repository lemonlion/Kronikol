#!/bin/sh
# MongoDB for the probe, in the podman WSL distro (this machine has no Docker pipe). From Git Bash:
#   MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/Code/Kronikol-mongo136/plans/MONGODB_ACCESSOR_OPTION_PLAN.harness/start-mongo.sh
# WSL forwards the port, so the probe on Windows reaches mongodb://127.0.0.1:27017. Remove with: podman rm -f mongo136
podman rm -f mongo136 >/dev/null 2>&1
podman run -d --name mongo136 -p 27017:27017 docker.io/library/mongo:7.0
