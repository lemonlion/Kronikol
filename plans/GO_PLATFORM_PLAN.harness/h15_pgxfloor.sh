#!/usr/bin/env bash
# H15b. What the capture module's pgx requirement does to its user's build, and whether one build of it
# can take pgconn's AfterNetConnect (pgx 5.8.0) where the user's pgx has it and fall back where not.
#
# (a) A module requiring pgx v5.8.0, added to a service on v5.5.0: which pgx the service then builds with.
# (b) One capture-module source that requires only v5.6.0 and sets AfterNetConnect by reflection,
#     built into a service on v5.6.0 and into one on v5.11.0.
set -u
NEW="${NEW:-go1.27.1}"
w="$(mktemp -d)"
trap 'rm -rf "$w"' EXIT
export GOTOOLCHAIN=$NEW GOFLAGS=-mod=mod

mod() { # dir module-path pgx-version [extra require/replace lines]
  mkdir -p "$1"
  { echo "module $2"; echo; echo "go 1.25.0"; echo; echo "require github.com/jackc/pgx/v5 $3"; shift 3; for l in "$@"; do echo "$l"; done; } > "$1/go.mod"
}

echo "=== H15b (a) a capture module requiring pgx v5.8.0, added to a service on v5.5.0"
mod "$w/a/kpgx" example.com/kpgx v5.8.0
printf 'package kpgx\n\nimport _ "github.com/jackc/pgx/v5/pgconn"\n' > "$w/a/kpgx/kpgx.go"
mod "$w/a/svc" example.com/svc v5.5.0
printf 'package main\n\nimport _ "github.com/jackc/pgx/v5"\n\nfunc main() {}\n' > "$w/a/svc/main.go"
(cd "$w/a/svc" && go mod tidy >/dev/null 2>&1 && echo "  the service alone builds with: $(go list -m github.com/jackc/pgx/v5)")
printf 'require example.com/kpgx v0.0.0\n\nreplace example.com/kpgx => ../kpgx\n' >> "$w/a/svc/go.mod"
printf 'package main\n\nimport _ "example.com/kpgx"\n' > "$w/a/svc/main_test.go"
(cd "$w/a/svc" && go mod tidy >/dev/null 2>&1 && echo "  with the module in its tests only:  $(go list -m github.com/jackc/pgx/v5)" \
  && echo "  and its production binary links:   $(go build -o bin . && go version -m bin | awk '$2=="github.com/jackc/pgx/v5"{print $3}')")

echo "=== H15b (b) one capture-module source requiring v5.6.0, AfterNetConnect set by reflection"
mod "$w/b/kpgx" example.com/kpgx v5.6.0
cat > "$w/b/kpgx/kpgx.go" <<'EOF'
package kpgx

import (
	"context"
	"net"
	"reflect"

	"github.com/jackc/pgx/v5/pgconn"
)

// Tap installs wrap on the connection after TLS where this pgx has AfterNetConnect, and says which seam it took.
func Tap(cfg *pgconn.Config, wrap func(net.Conn) net.Conn) string {
	f := reflect.ValueOf(cfg).Elem().FieldByName("AfterNetConnect")
	if !f.IsValid() {
		return "no AfterNetConnect in this pgx: BuildFrontend"
	}
	f.Set(reflect.ValueOf(func(_ context.Context, _ *pgconn.Config, c net.Conn) (net.Conn, error) { return wrap(c), nil }))
	return "AfterNetConnect set"
}
EOF
for v in v5.6.0 v5.11.0; do
  mod "$w/b/svc-$v" example.com/svc "$v" "require example.com/kpgx v0.0.0" "replace example.com/kpgx => ../kpgx"
  cat > "$w/b/svc-$v/main.go" <<'EOF'
package main

import (
	"fmt"
	"net"

	"example.com/kpgx"
	"github.com/jackc/pgx/v5/pgconn"
)

func main() {
	cfg, err := pgconn.ParseConfig("postgres://u@127.0.0.1:5432/db?sslmode=disable")
	if err != nil {
		panic(err)
	}
	fmt.Println(kpgx.Tap(cfg, func(c net.Conn) net.Conn { return c }))
}
EOF
  (cd "$w/b/svc-$v" && go mod tidy >/dev/null 2>&1 && echo "  service on $(go list -m github.com/jackc/pgx/v5): $(go run . 2>&1)")
done
