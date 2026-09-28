// Package broken does not compile, on purpose: what does `go test -json` say about a package whose
// test binary never runs?
package broken

import "testing"

func TestNeverRuns(t *testing.T) {
	undefinedFunction()
}
