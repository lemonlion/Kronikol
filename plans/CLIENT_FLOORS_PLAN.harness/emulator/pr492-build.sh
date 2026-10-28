#!/bin/sh
# Build goccy/bigquery-emulator PR #492 (lraigosov:feat/maintenance-and-fixes @ c80afb507e) and start it on :19060.
# The head does not build as-is (grpc v1.81.1 against x/net v0.54.0: undefined http2.TrailerPrefix), so a second try
# moves grpc to its newest release; the emulator's own code is unchanged.
podman rm -f bq492build bq492 >/dev/null 2>&1 || true
podman volume create bq492out >/dev/null 2>&1 || true
podman run --name bq492build -v bq492out:/out docker.io/library/golang:bookworm sh -c '
  git clone -q --depth 1 -b feat/maintenance-and-fixes https://github.com/lraigosov/bigquery-emulator /b && cd /b &&
  git log --oneline -1 &&
  if CGO_ENABLED=0 go build -o /out/bigquery-emulator ./cmd/bigquery-emulator; then echo BUILT-AS-IS;
  else echo RETRY-WITH-NEWEST-GRPC && go get google.golang.org/grpc@latest && CGO_ENABLED=0 go build -o /out/bigquery-emulator ./cmd/bigquery-emulator && echo BUILT-WITH-NEWEST-GRPC; fi;
  ls -la /out'
podman rm -f bq492build >/dev/null 2>&1 || true
podman run -d --name bq492 -p 19060:9050 -v bq492out:/out docker.io/library/debian:bookworm /out/bigquery-emulator --project=probe
sleep 5
podman logs bq492 2>&1 | tail -3
echo BUILD-DONE
