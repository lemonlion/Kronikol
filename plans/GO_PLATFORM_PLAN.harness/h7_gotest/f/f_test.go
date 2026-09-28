// Package f: a failing assertion in the Go idiom, and one in testify's shape, for the failure text a
// capturer hands the renderer.
package f

import "testing"

func TestFail(t *testing.T) {
	got, want := 3, 4
	if got != want {
		t.Errorf("Total() = %d, want %d", got, want) // the Go idiom: "got X, want Y"
	}
}
