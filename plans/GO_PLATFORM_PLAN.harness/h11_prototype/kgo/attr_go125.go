//go:build go1.25

package kgo

import (
	"context"
	"testing"
)

// attr links the Go test to its scenario in `go test -json` ("Action":"attr", H9b), so a CI tool
// or the run's wrapper can join the two without reading any capture file.
func attr(t testing.TB, key, value string) { t.Attr(key, value) }

func testContext(t testing.TB) context.Context { return t.Context() }
