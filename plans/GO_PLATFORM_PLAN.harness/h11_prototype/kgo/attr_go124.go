//go:build go1.24 && !go1.25

package kgo

import (
	"context"
	"testing"
)

func attr(testing.TB, string, string) {} // T.Attr arrives in Go 1.25

func testContext(t testing.TB) context.Context { return t.Context() } // T.Context arrived in Go 1.24
