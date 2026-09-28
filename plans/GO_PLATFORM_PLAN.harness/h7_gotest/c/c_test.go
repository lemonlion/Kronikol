// Package c: a test that outlives -timeout. Does t.Cleanup run when the binary times out?
package c

import (
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestSlow(t *testing.T) {
	t.Cleanup(func() {
		os.WriteFile(filepath.Join(os.Getenv("H7_MARKERS"), "c-cleanup-ran"), nil, 0o644)
	})
	time.Sleep(5 * time.Second)
}
