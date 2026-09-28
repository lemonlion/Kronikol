// Package e: TestMain, the only per-process run hook the testing package offers, and a pid so the
// summary can show one process per package.
package e

import (
	"fmt"
	"os"
	"testing"
	"time"
)

func TestMain(m *testing.M) {
	fmt.Printf("TestMain enter pid=%d\n", os.Getpid())
	code := m.Run()
	fmt.Printf("TestMain exit code=%d\n", code)
	os.Exit(code)
}

func TestSlowish(t *testing.T) { time.Sleep(300 * time.Millisecond); t.Logf("pid %d", os.Getpid()) }
