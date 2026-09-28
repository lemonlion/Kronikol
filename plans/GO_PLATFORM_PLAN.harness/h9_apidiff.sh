#!/usr/bin/env bash
# H9. What the standard library a Go capturer leans on gained between the local toolchain and the
# newest release: exported funcs, methods and types, per package, as `go doc -all` prints them.
set -u
old="${OLD:-go1.24.7}"; new="${NEW:-go1.27.1}"
pkgs="testing testing/synctest net/http net/http/httptest net/http/httptrace database/sql database/sql/driver runtime/pprof context log/slog"
api() { GOTOOLCHAIN="$1" go doc -all "$2" 2>/dev/null | grep -E '^(func|type) ' | sed 's/^ *//' | sed 's/ {$//' | sort -u; }
for p in $pkgs; do
  a=$(mktemp); b=$(mktemp)
  api "$old" "$p" > "$a"; api "$new" "$p" > "$b"
  added=$(comm -13 "$a" "$b"); removed=$(comm -23 "$a" "$b")
  echo "== $p: $(echo -n "$added" | grep -c . ) added, $(echo -n "$removed" | grep -c .) removed ($old -> $new)"
  [ -n "$added" ] && echo "$added" | sed 's/^/  + /'
  [ -n "$removed" ] && echo "$removed" | sed 's/^/  - /'
  rm -f "$a" "$b"
done
echo "== which release added each API the plan uses"
for s in testing.T.Attr testing.T.Output testing.T.ArtifactDir testing/synctest.Test testing/synctest.Sleep \
         net/http/httptest.NewTestServer database/sql/driver.RowsColumnScanner net/http.Transport.NewClientConn; do
  first=""
  for v in go1.25.0 go1.26.0 go1.27.0; do
    if GOTOOLCHAIN=$v go doc "$s" >/dev/null 2>&1; then first=$v; break; fi
  done
  echo "  $s: first in ${first:-none of 1.25 to 1.27}"
done
echo "== testing.T's methods on $new (none returns what the test logged)"
GOTOOLCHAIN="$new" go doc testing.T 2>/dev/null | grep -E '^func \((c|t) \*T\)' | sed 's/^func /  /'
