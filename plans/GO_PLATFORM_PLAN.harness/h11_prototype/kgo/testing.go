package kgo

import (
	"context"
	"runtime"
	"strings"
	"testing"
	"time"
)

// Main is the optional TestMain hook: the run markers ingest reads for --run-window. Go has no other
// per-process hook, and a package's binary is one process of many (H7 S1, S7), so the report is
// rendered after `go test` returns, never from here.
func Main(m *testing.M) int {
	writeTest(TestEvent{Event: "testrun", TestID: "__run__", Status: "started", Timestamp: now()})
	code := m.Run()
	status := "passed"
	if code != 0 {
		status = "failed"
	}
	writeTest(TestEvent{Event: "testrun", TestID: "__run__", Status: status, Timestamp: now()})
	return code
}

// Start opens a scenario for t and returns the context that carries it. Everything the test does
// with that context, and everything a Handler serves for its requests, is attributed to it.
func Start(t testing.TB) context.Context {
	t.Helper()
	// Read again here, inside a test: go test keys its cache on the environment variables a test
	// reads, and a read in TestMain before m.Run() is not recorded (harness H11, run 3).
	_ = RunDir()
	id := Identity{ID: randHex(16), Name: t.Name()} // 32 hex: also the W3C trace id, as TestTrackingIdentity does
	start := time.Now()
	ev := TestEvent{Event: "start", TestID: id.ID, TestName: t.Name(), Feature: callerPackage(), Timestamp: now()}
	// Table-driven subtests are Go's scenario outlines: the parent test is the outline, the case
	// name its example (t.Name() has already mangled it: spaces to underscores, H7 S1).
	if parent, sub, ok := strings.Cut(t.Name(), "/"); ok {
		ev.TestName, ev.OutlineID = t.Name(), parent
		ev.ExampleValues = map[string]string{"case": strings.ReplaceAll(sub, "_", " ")}
	}
	writeTest(ev)
	attr(t, "kronikol.testId", id.ID)

	mu.Lock()
	running[id.ID] = id
	mu.Unlock()
	t.Cleanup(func() {
		pending.flush(id.ID)
		status := "passed"
		switch {
		case t.Skipped():
			status = "skipped"
		case t.Failed():
			status = "failed"
		}
		writeTest(TestEvent{Event: "end", TestID: id.ID, Status: status, Timestamp: now(),
			DurationMs: float64(time.Since(start).Microseconds()) / 1000})
		mu.Lock()
		delete(running, id.ID)
		ended[id.ID] = true
		mu.Unlock()
	})

	ctx := context.WithValue(testContext(t), identityKey{}, scoped{id, SourceTestContext})
	label(ctx, id)
	return ctx
}

// Step records a top-level step (a delimiter bar in the diagram) and runs fn inside it.
func Step(ctx context.Context, keyword, text string, fn func(context.Context)) {
	id, _ := resolve(ctx)
	writeTest(TestEvent{Event: "step", TestID: id.ID, Keyword: keyword, Text: text, Timestamp: now()})
	fn(ctx)
}

// callerPackage is the import path of the test calling Start: the feature a Go scenario sits under.
func callerPackage() string {
	pc, _, _, ok := runtime.Caller(2)
	if !ok {
		return "go test"
	}
	name := runtime.FuncForPC(pc).Name() // h11/orders.TestPlaceOrder.func1
	slash := strings.LastIndex(name, "/")
	if dot := strings.Index(name[slash+1:], "."); dot >= 0 {
		name = name[:slash+1+dot]
	}
	// An external test package (package orders_test) is compiled as "h11/orders_test"; go test's
	// -json stream names it "h11/orders". The feature is the package under test.
	return strings.TrimSuffix(name, "_test")
}
