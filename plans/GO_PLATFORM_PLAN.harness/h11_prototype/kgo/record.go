// Package kgo is a throwaway prototype of the Go capturer GO_PLATFORM_PLAN.md designs: enough to
// write the two NDJSON streams of the existing contract (InteractionRecord, TestRunRecord) from a Go
// test run, so that today's `kronikol ingest` can render them. Standard library only. Not a product:
// no options channel, no redaction list beyond two headers, no optional-interface fidelity for SQL
// rows (the plan's §3.4 says why that matters).
package kgo

import (
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sync"
	"sync/atomic"
	"time"
)

// Header is one header on the wire: {"key": ..., "value": ...}.
type Header struct {
	Key   string `json:"key"`
	Value string `json:"value"`
}

// Interaction is one line of interactions NDJSON: InteractionRecord's wire shape.
type Interaction struct {
	Type               string   `json:"type"`
	Method             string   `json:"method,omitempty"`
	URI                string   `json:"uri"`
	ServiceName        string   `json:"serviceName"`
	CallerName         string   `json:"callerName"`
	Content            string   `json:"content,omitempty"`
	Headers            []Header `json:"headers,omitempty"`
	StatusCode         string   `json:"statusCode,omitempty"`
	TraceID            string   `json:"traceId,omitempty"`
	RequestResponseID  string   `json:"requestResponseId,omitempty"`
	Timestamp          string   `json:"timestamp,omitempty"`
	TestID             string   `json:"testId"`
	TestName           string   `json:"testName,omitempty"`
	DependencyCategory string   `json:"dependencyCategory,omitempty"`
	DurationMs         float64  `json:"durationMs,omitempty"`
	ActivityTraceID    string   `json:"activityTraceId,omitempty"`
	ActivitySpanID     string   `json:"activitySpanId,omitempty"`
	// Extras the contract does not have yet (unknown properties are ignored on ingest). The plan's
	// §4 asks for them.
	AttributionSource string `json:"attributionSource,omitempty"`
	Seq               int64  `json:"seq"`
}

// TestEvent is one line of tests NDJSON: TestRunRecord's wire shape (the fields this prototype uses).
type TestEvent struct {
	Event         string            `json:"event"`
	TestID        string            `json:"testId"`
	TestName      string            `json:"testName,omitempty"`
	Feature       string            `json:"feature,omitempty"`
	Timestamp     string            `json:"timestamp,omitempty"`
	Status        string            `json:"status,omitempty"`
	DurationMs    float64           `json:"durationMs,omitempty"`
	Error         string            `json:"error,omitempty"`
	Text          string            `json:"text,omitempty"`
	Keyword       string            `json:"keyword,omitempty"`
	Level         int               `json:"level,omitempty"`
	OutlineID     string            `json:"outlineId,omitempty"`
	ExampleValues map[string]string `json:"exampleValues,omitempty"`
}

// stream appends one JSON object per line with one write(2) per line, so a panic that kills the
// process loses nothing already written (H7: a panic ends the binary; a timeout skips Cleanup).
type stream struct {
	mu sync.Mutex
	f  *os.File
}

func (s *stream) write(v any) {
	line, err := json.Marshal(v)
	if err != nil {
		panic(err)
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	s.f.Write(append(line, '\n'))
}

var (
	openOnce     sync.Once
	interactions *stream
	tests        *stream
	seq          atomic.Int64 // the ordering key a clock cannot be under testing/synctest (H8)
)

// RunDir is KRONIKOL_RUN_DIR, the Node plan's name for the same thing, or ./.kronikol beside the
// package under test. Reading it through os.Getenv also puts it in go test's cache key (H7 S3).
func RunDir() string {
	if d := os.Getenv("KRONIKOL_RUN_DIR"); d != "" {
		return d
	}
	return ".kronikol"
}

func open() {
	openOnce.Do(func() {
		dir := RunDir()
		// Two directories, not one: today's ingest reads every *.ndjson under its inputs as
		// interactions, and takes the tests stream from one --tests file (IngestCommand.cs:57-60).
		for _, sub := range []string{"interactions", "tests"} {
			if err := os.MkdirAll(filepath.Join(dir, sub), 0o755); err != nil {
				panic(err)
			}
		}
		name := fmt.Sprintf("%d-%d.ndjson", os.Getpid(), time.Now().UnixNano())
		mk := func(sub string) *stream {
			f, err := os.OpenFile(filepath.Join(dir, sub, name), os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o644)
			if err != nil {
				panic(err)
			}
			return &stream{f: f}
		}
		interactions, tests = mk("interactions"), mk("tests")
	})
}

func writeInteraction(r Interaction) {
	open()
	r.Seq = seq.Add(1)
	interactions.write(r)
}

func writeTest(e TestEvent) {
	open()
	tests.write(e)
}

func now() string { return time.Now().UTC().Format(time.RFC3339Nano) }

func randHex(n int) string {
	b := make([]byte, n)
	rand.Read(b)
	return hex.EncodeToString(b)
}

// newGUID formats 16 random bytes as a GUID: the .NET reader parses test-tracking-trace-id with Guid.Parse.
func newGUID() string {
	h := randHex(16)
	return h[0:8] + "-" + h[8:12] + "-" + h[12:16] + "-" + h[16:20] + "-" + h[20:32]
}
