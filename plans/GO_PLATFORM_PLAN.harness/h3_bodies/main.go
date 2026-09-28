// H3. Capturing bodies from a RoundTripper without changing what the caller sees.
// Two response strategies: EAGER (read up to the cap before returning, the .NET handler's
// behaviour: it awaits ReadContentAsStringAsync) and TEE (return at once, record what the caller
// reads, finish the record on EOF or Close). Plan §3.3 picks between them from this output.
package main

import (
	"bytes"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"time"
)

const capBytes = 64 << 10

type record struct {
	path     string
	reqBody  string
	respBody string
	capped   bool
	complete bool
}

type capture struct {
	mode    string // "eager" or "tee"
	next    http.RoundTripper
	mu      sync.Mutex
	records []*record
}

func (c *capture) add(r *record) { c.mu.Lock(); c.records = append(c.records, r); c.mu.Unlock() }

func (c *capture) pending() (n int) {
	c.mu.Lock()
	defer c.mu.Unlock()
	for _, r := range c.records {
		if !r.complete {
			n++
		}
	}
	return n
}

// RoundTrip honours the RoundTripper contract: it never mutates the caller's *http.Request; the
// body it reads is handed on through a clone.
func (c *capture) RoundTrip(req *http.Request) (*http.Response, error) {
	rec := &record{path: req.URL.Path}
	out := req
	if req.Body != nil && req.Body != http.NoBody {
		b, err := io.ReadAll(req.Body)
		req.Body.Close()
		if err != nil {
			return nil, err
		}
		rec.reqBody = string(b)
		out = req.Clone(req.Context())
		out.Body = io.NopCloser(bytes.NewReader(b))
		out.GetBody = func() (io.ReadCloser, error) { return io.NopCloser(bytes.NewReader(b)), nil }
	}
	resp, err := c.next.RoundTrip(out)
	if err != nil {
		return nil, err
	}
	c.add(rec)
	switch c.mode {
	case "eager":
		head, err := io.ReadAll(io.LimitReader(resp.Body, capBytes+1))
		if err != nil {
			return nil, err
		}
		rec.capped = len(head) > capBytes
		if rec.capped {
			rec.respBody = string(head[:capBytes])
		} else {
			rec.respBody = string(head)
		}
		rec.complete = true
		// The caller still reads every byte: what was read, then the rest of the stream.
		resp.Body = struct {
			io.Reader
			io.Closer
		}{io.MultiReader(bytes.NewReader(head), resp.Body), resp.Body}
	case "tee":
		resp.Body = &teeBody{rc: resp.Body, rec: rec, c: c}
	}
	return resp, nil
}

type teeBody struct {
	rc   io.ReadCloser
	rec  *record
	c    *capture
	buf  bytes.Buffer
	done bool
}

func (t *teeBody) Read(p []byte) (int, error) {
	n, err := t.rc.Read(p)
	if room := capBytes - t.buf.Len(); room > 0 {
		t.buf.Write(p[:min(n, room)])
	} else if n > 0 {
		t.rec.capped = true
	}
	if err == io.EOF {
		t.finish()
	}
	return n, err
}

func (t *teeBody) Close() error { t.finish(); return t.rc.Close() }

func (t *teeBody) finish() {
	if t.done {
		return
	}
	t.done = true
	t.c.mu.Lock()
	t.rec.respBody, t.rec.complete = t.buf.String(), true
	t.c.mu.Unlock()
}

func main() {
	mux := http.NewServeMux()
	mux.HandleFunc("/echo", func(w http.ResponseWriter, r *http.Request) { io.Copy(w, r.Body) })
	mux.HandleFunc("/redirect307", func(w http.ResponseWriter, r *http.Request) {
		http.Redirect(w, r, "/echo", http.StatusTemporaryRedirect)
	})
	mux.HandleFunc("/sse", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/event-stream")
		io.WriteString(w, "data: first\n\n")
		w.(http.Flusher).Flush()
		time.Sleep(1500 * time.Millisecond)
		io.WriteString(w, "data: second\n\n")
	})
	mux.HandleFunc("/big", func(w http.ResponseWriter, r *http.Request) { w.Write(bytes.Repeat([]byte("x"), 1<<20)) })
	srv := httptest.NewServer(mux)
	defer srv.Close()

	for _, mode := range []string{"eager", "tee"} {
		c := &capture{mode: mode, next: http.DefaultTransport}
		client := &http.Client{Transport: c}
		fmt.Printf("H3 mode=%s\n", mode)

		resp, _ := client.Post(srv.URL+"/echo", "text/plain", strings.NewReader("order-42"))
		got, _ := io.ReadAll(resp.Body)
		resp.Body.Close()
		r := c.records[len(c.records)-1]
		fmt.Printf("  POST /echo: caller got %q; record req=%q resp=%q\n", got, r.reqBody, r.respBody)

		resp, _ = client.Post(srv.URL+"/redirect307", "text/plain", bytes.NewReader([]byte("order-43")))
		got, _ = io.ReadAll(resp.Body)
		resp.Body.Close()
		fmt.Printf("  POST /redirect307 (body replayed through GetBody): status=%d caller got %q; records=%d\n", resp.StatusCode, got, len(c.records))

		start := time.Now()
		resp, _ = client.Get(srv.URL + "/sse")
		returned := time.Since(start)
		line := make([]byte, 13)
		io.ReadFull(resp.Body, line)
		firstEvent := time.Since(start)
		io.Copy(io.Discard, resp.Body)
		resp.Body.Close()
		fmt.Printf("  GET /sse (event 2 comes 1.5 s after event 1): RoundTrip returned after %v, caller read %q after %v\n",
			returned.Round(100*time.Millisecond), line, firstEvent.Round(100*time.Millisecond))

		resp, _ = client.Get(srv.URL + "/big")
		n, _ := io.Copy(io.Discard, resp.Body)
		resp.Body.Close()
		r = c.records[len(c.records)-1]
		fmt.Printf("  GET /big (1 MiB): caller read %d bytes; record kept %d bytes, capped=%v\n", n, len(r.respBody), r.capped)

		resp, _ = client.Post(srv.URL+"/echo", "text/plain", strings.NewReader("never-read"))
		_ = resp // the caller checks the status and never reads or closes the body
		fmt.Printf("  POST /echo, body never read or closed: records still pending = %d\n", c.pending())
	}
}
