#!/bin/sh
podman rm -f k193-pg >/dev/null 2>&1
podman run -d --name k193-pg -p 55432:5432 -e POSTGRES_PASSWORD=k193pass docker.io/library/postgres:17-alpine
sleep 6
podman exec k193-pg pg_isready -U postgres
