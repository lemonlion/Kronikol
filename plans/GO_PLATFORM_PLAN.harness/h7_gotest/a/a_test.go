// Package a: the ordinary shapes a Go suite has, for the -json event stream, the name mangling and
// the ordering of t.Context() against t.Cleanup.
package a

import (
	"context"
	"fmt"
	"os"
	"testing"
	"time"
)

func TestPass(t *testing.T) {}

func TestSkip(t *testing.T) { t.Skip("needs a real broker") }

func TestTable(t *testing.T) {
	for _, name := range []string{"valid order", "dup", "dup", "a/b", "émoji ✓"} {
		t.Run(name, func(t *testing.T) { t.Logf("t.Name() = %q", t.Name()) })
	}
}

func TestParallel(t *testing.T) {
	for _, name := range []string{"left", "right"} {
		t.Run(name, func(t *testing.T) {
			t.Parallel()
			time.Sleep(200 * time.Millisecond)
		})
	}
}

type key struct{}

func TestContextBeforeCleanup(t *testing.T) {
	ctx := context.WithValue(t.Context(), key{}, "T1")
	t.Cleanup(func() {
		fmt.Printf("cleanup: ctx.Err() = %v, ctx.Value = %v, t.Failed() = %v\n", ctx.Err(), ctx.Value(key{}), t.Failed())
	})
}

// Reads an environment variable the way a capturer reading KRONIKOL_RUN_DIR would.
func TestReadsEnv(t *testing.T) {
	t.Logf("H7_RUN_ID = %q, pid %d", os.Getenv("H7_RUN_ID"), os.Getpid())
}
