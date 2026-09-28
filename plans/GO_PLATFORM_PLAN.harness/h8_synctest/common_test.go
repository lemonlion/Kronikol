// H8. testing/synctest runs a test in a bubble whose clock is fake. A capture timestamp taken inside
// the bubble is the fake time, not the wall clock, and records written outside the bubble sort
// around it by the real one.
package h8

import "time"

var fakeEpoch = time.Date(2000, 1, 1, 0, 0, 0, 0, time.UTC)

// rebase maps a fake bubble time onto the wall clock: the bubble's clock starts at fakeEpoch, so the
// offset from it is the time the code inside believes has passed.
func rebase(fake, bubbleRealStart time.Time) time.Time {
	return bubbleRealStart.Add(fake.Sub(fakeEpoch))
}
