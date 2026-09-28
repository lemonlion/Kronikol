package kgo

import (
	"context"
	"os"
	"runtime/pprof"
	"sync"
	"unsafe"
)

// Identity is a scenario: the id every record carries and the display name.
type Identity struct{ ID, Name string }

// Attribution sources, named as .NET's AttributionSource enum names them, plus GoroutineLabel, which
// has no .NET counterpart (AsyncLocal flows on its own; a goroutine only inherits a pprof label).
const (
	SourceRequestHeader  = "RequestHeader"
	SourceTestContext    = "TestContext"
	SourceScope          = "Scope"
	SourceGoroutineLabel = "GoroutineLabel"
	SourceGlobalFallback = "GlobalFallback"
	SourceExpired        = "Expired"
	SourceNone           = "None"
)

type scoped struct {
	id     Identity
	source string
}

type (
	identityKey struct{}
	sutKey      struct{} // set by Handler: the service whose request this goroutine is serving
	capturedKey struct{} // set by Transport on the request it forwards, so a global hook does not record it twice
)

// WithIdentity returns ctx carrying id: the Go form of .NET's TestIdentityScope.Begin.
func WithIdentity(ctx context.Context, id Identity) context.Context {
	return context.WithValue(ctx, identityKey{}, scoped{id, SourceScope})
}

var (
	mu      sync.Mutex
	running = map[string]Identity{} // tests started and not yet ended, in this process
	ended   = map[string]bool{}

	// Goroutine-label fallback (H5), off unless KRONIKOL_GOROUTINE_LABELS=1: the label pointer a
	// goroutine inherits, keyed to the identity that set it. No label layout is decoded.
	labelsOn = os.Getenv("KRONIKOL_GOROUTINE_LABELS") == "1"
	byLabel  = map[unsafe.Pointer]labelled{}
)

// labelled is what a goroutine inherits: the scenario and, inside a Handler, the service serving it.
type labelled struct {
	id      Identity
	service string
}

//go:linkname getProfLabel runtime/pprof.runtime_getProfLabel
func getProfLabel() unsafe.Pointer

// label puts id on the current goroutine, so goroutines it starts from now on inherit it.
func label(ctx context.Context, id Identity) {
	if !labelsOn {
		return
	}
	pprof.SetGoroutineLabels(pprof.WithLabels(ctx, pprof.Labels("kronikol.test", id.ID)))
	service, _ := ctx.Value(sutKey{}).(string)
	mu.Lock()
	byLabel[getProfLabel()] = labelled{id, service}
	mu.Unlock()
}

// resolve is the Go cascade: the context first (headers read by Handler, the test's own context,
// an explicit scope), then the goroutine's inherited label, then the only test running in this
// process. A scenario that has ended answers as Expired, as on .NET.
func resolve(ctx context.Context) (Identity, string) {
	id, source := resolveRaw(ctx)
	if source != SourceNone {
		mu.Lock()
		gone := ended[id.ID]
		mu.Unlock()
		if gone {
			return id, SourceExpired
		}
	}
	return id, source
}

func resolveRaw(ctx context.Context) (Identity, string) {
	if s, ok := ctx.Value(identityKey{}).(scoped); ok {
		return s.id, s.source
	}
	mu.Lock()
	defer mu.Unlock()
	if labelsOn {
		if l, ok := byLabel[getProfLabel()]; ok {
			return l.id, SourceGoroutineLabel
		}
	}
	if len(running) == 1 {
		for _, id := range running {
			return id, SourceGlobalFallback
		}
	}
	return Identity{}, SourceNone
}

// callerOf names the calling participant: the service a Handler is serving (from the context, or
// inherited through the goroutine's label), else fallback.
func callerOf(ctx context.Context, fallback string) string {
	if s, ok := ctx.Value(sutKey{}).(string); ok {
		return s
	}
	if labelsOn {
		mu.Lock()
		l, ok := byLabel[getProfLabel()]
		mu.Unlock()
		if ok && l.service != "" {
			return l.service
		}
	}
	if fallback != "" {
		return fallback
	}
	return "Caller"
}
