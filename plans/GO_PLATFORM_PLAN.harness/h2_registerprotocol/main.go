// H2. Transport.RegisterProtocol as a type-preserving hook: http.DefaultTransport stays a
// *http.Transport (so H1's type assertions keep working) while every http:// request through it
// reaches a recording RoundTripper. What it covers, and where HTTP/2 already owns "https".
package main

import (
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"sync"
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

// Built at package initialisation, before anything could register: holds the same pointer.
var initTimeClient = &http.Client{Transport: http.DefaultTransport}

func register(t *http.Transport, scheme string, rt http.RoundTripper) (err error) {
	defer func() {
		if r := recover(); r != nil {
			err = fmt.Errorf("panic: %v", r)
		}
	}()
	t.RegisterProtocol(scheme, rt)
	return nil
}

func main() {
	plain := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { io.WriteString(w, "ok") }))
	defer plain.Close()

	dt := http.DefaultTransport.(*http.Transport)
	// A fresh transport does the real work: a clone taken after registration would be handed the
	// registration too (checked below), and a recorder that calls back into dt would recurse.
	inner := &http.Transport{Proxy: dt.Proxy, DialContext: dt.DialContext, ForceAttemptHTTP2: dt.ForceAttemptHTTP2,
		MaxIdleConns: dt.MaxIdleConns, IdleConnTimeout: dt.IdleConnTimeout,
		TLSHandshakeTimeout: dt.TLSHandshakeTimeout, ExpectContinueTimeout: dt.ExpectContinueTimeout}
	rec := &recorder{next: inner}

	fmt.Println("H2 RegisterProtocol on the untouched default transport:")
	fmt.Printf("  RegisterProtocol(\"http\", recorder): err = %v\n", register(dt, "http", rec))

	try := func(label string, do func() (*http.Response, error)) {
		before := rec.count()
		resp, err := do()
		if err == nil {
			io.Copy(io.Discard, resp.Body)
			resp.Body.Close()
		}
		fmt.Printf("  %-58s captured=%-5v err=%v\n", label, rec.count() > before, err)
	}
	try("http.Get(http://...)", func() (*http.Response, error) { return http.Get(plain.URL) })
	try("(&http.Client{}).Get(http://...)", func() (*http.Response, error) { return (&http.Client{}).Get(plain.URL) })
	try("package-level client built at init (same *http.Transport)", func() (*http.Response, error) { return initTimeClient.Get(plain.URL) })

	_, ok := http.DefaultTransport.(*http.Transport)
	fmt.Printf("  http.DefaultTransport.(*http.Transport) still ok = %v\n", ok)

	clone := dt.Clone()
	try("client on dt.Clone() taken after registration", func() (*http.Response, error) {
		return (&http.Client{Transport: clone}).Get(plain.URL)
	})

	fmt.Printf("  RegisterProtocol(\"https\", recorder): err = %v\n", register(dt, "https", rec))
	fresh := &http.Transport{}
	fmt.Printf("  on a fresh &http.Transport{} (never used): RegisterProtocol(\"https\"): err = %v\n", register(fresh, "https", rec))
	h2 := &http.Transport{ForceAttemptHTTP2: true}
	_ = h2.Clone() // Clone runs the lazy HTTP/2 set-up
	fmt.Printf("  on &http.Transport{ForceAttemptHTTP2: true} after Clone(): RegisterProtocol(\"https\"): err = %v\n", register(h2, "https", rec))
}
