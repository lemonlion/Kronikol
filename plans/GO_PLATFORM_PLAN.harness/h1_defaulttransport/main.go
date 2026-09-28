// H1. What swapping http.DefaultTransport for a recording wrapper captures, what it misses,
// and what it breaks. Plan §3.3 (the global switch) rests on this output.
package main

import (
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"sync"
	"time"
)

type recorder struct {
	next http.RoundTripper
	mu   sync.Mutex
	seen int
}

func (r *recorder) RoundTrip(req *http.Request) (*http.Response, error) {
	r.mu.Lock()
	r.seen++
	r.mu.Unlock()
	return r.next.RoundTrip(req)
}

func (r *recorder) count() int { r.mu.Lock(); defer r.mu.Unlock(); return r.seen }

// Built at package initialisation, before any TestMain could swap the transport:
// the common `var client = &http.Client{Transport: http.DefaultTransport}` shape.
var initTimeClient = &http.Client{Transport: http.DefaultTransport}

func main() {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { io.WriteString(w, "ok") }))
	defer srv.Close()

	rec := &recorder{next: http.DefaultTransport}
	http.DefaultTransport = rec

	try := func(label string, do func() (*http.Response, error)) {
		before := rec.count()
		resp, err := do()
		if err == nil {
			io.Copy(io.Discard, resp.Body)
			resp.Body.Close()
		}
		fmt.Printf("  %-66s captured=%-5v err=%v\n", label, rec.count() > before, err)
	}
	fmt.Println("H1 clients after `http.DefaultTransport = wrapper`:")
	try("http.Get", func() (*http.Response, error) { return http.Get(srv.URL) })
	try("http.DefaultClient.Get", func() (*http.Response, error) { return http.DefaultClient.Get(srv.URL) })
	try("(&http.Client{Timeout: 5s}).Get  (Transport left nil)", func() (*http.Response, error) {
		return (&http.Client{Timeout: 5 * time.Second}).Get(srv.URL)
	})
	try("(&http.Client{Transport: http.DefaultTransport}).Get  (built after the swap)", func() (*http.Response, error) {
		return (&http.Client{Transport: http.DefaultTransport}).Get(srv.URL)
	})
	try("package-level client built at init with Transport: http.DefaultTransport", func() (*http.Response, error) {
		return initTimeClient.Get(srv.URL)
	})
	try("(&http.Client{Transport: &http.Transport{}}).Get  (own transport)", func() (*http.Response, error) {
		return (&http.Client{Transport: &http.Transport{}}).Get(srv.URL)
	})
	try("srv.Client().Get  (httptest's own transport)", func() (*http.Response, error) { return srv.Client().Get(srv.URL) })

	fmt.Println("H1 code that type-asserts the default transport:")
	func() {
		defer func() { fmt.Printf("  http.DefaultTransport.(*http.Transport).Clone(): panic = %v\n", recover()) }()
		_ = http.DefaultTransport.(*http.Transport).Clone()
	}()
	t, ok := http.DefaultTransport.(*http.Transport)
	fmt.Printf("  t, ok := http.DefaultTransport.(*http.Transport): ok = %v, t == nil = %v (the caller silently takes its fallback)\n", ok, t == nil)
}
