// H5. pprof goroutine labels as an identity that child goroutines inherit, read back without a
// context.Context. Go gives goroutines no local storage; the runtime does copy the creating
// goroutine's label pointer into every goroutine it starts. This probe reads that pointer through
// the one runtime symbol Go keeps linkable for exactly this use (runtime/proflabel.go, "hall of
// shame", go.dev/issue/67401) and keys a registry on it, so nothing depends on the label layout.
package main

import (
	"context"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"runtime"
	"runtime/pprof"
	"sync"
	"unsafe"
)

//go:linkname getProfLabel runtime/pprof.runtime_getProfLabel
func getProfLabel() unsafe.Pointer

var (
	mu       sync.Mutex
	registry = map[unsafe.Pointer]string{}
)

// enter labels the current goroutine for test id and registers the label pointer the runtime now holds.
func enter(ctx context.Context, id string) context.Context {
	ctx = pprof.WithLabels(ctx, pprof.Labels("kronikol.test", id))
	pprof.SetGoroutineLabels(ctx)
	mu.Lock()
	registry[getProfLabel()] = id
	mu.Unlock()
	return ctx
}

func who() string {
	p := getProfLabel()
	if p == nil {
		return "<no labels>"
	}
	mu.Lock()
	defer mu.Unlock()
	if id, ok := registry[p]; ok {
		return id
	}
	return "<labels not registered by kronikol>"
}

func main() {
	fmt.Printf("H5 %s\n", runtime.Version())

	// A worker started before any test: it inherits nothing.
	work := make(chan chan string)
	go func() {
		for reply := range work {
			reply <- who()
		}
	}()

	ctx := enter(context.Background(), "T1")
	fmt.Printf("  test goroutine after enter(T1):                       %s\n", who())

	var wg sync.WaitGroup
	wg.Add(1)
	go func() {
		defer wg.Done()
		fmt.Printf("  child goroutine (no ctx passed):                      %s\n", who())
		var inner sync.WaitGroup
		inner.Add(1)
		go func() {
			defer inner.Done()
			fmt.Printf("  grandchild goroutine (no ctx passed):                 %s\n", who())
		}()
		inner.Wait()
	}()
	wg.Wait()

	reply := make(chan string)
	work <- reply
	fmt.Printf("  worker started before enter(T1), fed by channel:      %s\n", <-reply)

	// A server started while T1's labels are on the goroutine: its accept loop and every connection
	// goroutine inherit T1, whichever test sends the request later.
	var handlerSaw, afterReset string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		handlerSaw = who()
		enter(r.Context(), r.Header.Get("test-tracking-current-test-id")) // what the middleware would do
		afterReset = who()
		io.WriteString(w, "ok")
	}))
	defer srv.Close()
	enter(ctx, "T2") // the next test starts on this goroutine
	req, _ := http.NewRequest("GET", srv.URL, nil)
	req.Header.Set("test-tracking-current-test-id", "T2")
	resp, err := http.DefaultClient.Do(req)
	if err == nil {
		resp.Body.Close()
	}
	fmt.Printf("  handler of a server started under T1, request from T2: %s (stale)\n", handlerSaw)
	fmt.Printf("  same handler after the middleware re-labels from headers: %s\n", afterReset)

	// User code that sets its own labels replaces the pointer: the registry no longer knows it.
	pprof.Do(ctx, pprof.Labels("user", "profiling"), func(context.Context) {
		fmt.Printf("  inside the user's own pprof.Do:                       %s\n", who())
	})
	fmt.Printf("  after pprof.Do returns:                               %s\n", who())
}
