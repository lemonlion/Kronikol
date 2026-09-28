//go:build goexperiment.synctest && !go1.25

package h8

import (
	"testing"
	"testing/synctest"
	"time"
)

func TestFakeClock(t *testing.T) {
	outside := time.Now()
	t.Logf("outside the bubble:                      %s", outside.UTC().Format(time.RFC3339Nano))
	realStart := time.Now()
	synctest.Run(func() {
		first := time.Now()
		t.Logf("inside, first capture stamp:             %s", first.UTC().Format(time.RFC3339Nano))
		time.Sleep(5 * time.Second)
		second := time.Now()
		t.Logf("inside, after a 5 s sleep:               %s", second.UTC().Format(time.RFC3339Nano))
		t.Logf("second stamp rebased onto the wall clock: %s", rebase(second, realStart).UTC().Format(time.RFC3339Nano))
	})
	t.Logf("real time the bubble took:               %v", time.Since(realStart).Round(time.Millisecond))
}
