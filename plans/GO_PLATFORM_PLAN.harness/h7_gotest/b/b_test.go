// Package b: a test that panics. Does t.Cleanup run, and what happens to the tests after it?
package b

import (
	"os"
	"path/filepath"
	"testing"
)

func TestBeforePanic(t *testing.T) {}

func TestPanics(t *testing.T) {
	t.Cleanup(func() {
		os.WriteFile(filepath.Join(os.Getenv("H7_MARKERS"), "b-cleanup-ran"), nil, 0o644)
	})
	var m map[string]int
	m["boom"] = 1 // assignment to entry in nil map
}

func TestAfterPanic(t *testing.T) {}
