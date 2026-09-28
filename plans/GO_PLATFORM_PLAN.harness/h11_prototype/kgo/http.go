package kgo

import (
	"bytes"
	"context"
	"io"
	"net/http"
	"strconv"
	"strings"
	"sync"
	"time"
)

// The contract's header names (Kronikol.Constants.TestTrackingHttpHeaders).
const (
	HeaderTestName = "test-tracking-current-test-name"
	HeaderTestID   = "test-tracking-current-test-id"
	HeaderCaller   = "test-tracking-caller-name"
	HeaderTraceID  = "test-tracking-trace-id"
)

const bodyCap = 64 << 10

// Option names the parties of a wrapped client or handler.
type Option func(*options)

type options struct{ service, caller, category string }

// Service names the receiving participant. Under httptest.NewTestServer every server is
// "example.com" (H9b), so the name comes from here, never from the URL.
func Service(name string) Option { return func(o *options) { o.service = name } }

// Caller names the calling participant when the context does not (a Handler's context does).
func Caller(name string) Option { return func(o *options) { o.caller = name } }

// Category sets dependencyCategory (PostgreSQL, SqlServer, ...) for participant shape and colour.
func Category(name string) Option { return func(o *options) { o.category = name } }

func build(opts []Option) options {
	var o options
	for _, f := range opts {
		f(&o)
	}
	return o
}

// Transport wraps a RoundTripper: it stamps the identity headers, records the request and the
// response with their bodies, and hands the caller exactly the bytes the server sent.
func Transport(next http.RoundTripper, opts ...Option) http.RoundTripper {
	if next == nil {
		next = http.DefaultTransport
	}
	return &transport{next: next, o: build(opts)}
}

type transport struct {
	next http.RoundTripper
	o    options
}

func (t *transport) RoundTrip(req *http.Request) (*http.Response, error) {
	return roundTrip(t.next, t.o, req)
}

func roundTrip(next http.RoundTripper, o options, req *http.Request) (*http.Response, error) {
	// Nothing may name a scenario (source None): the call is recorded anyway, with no testId, for
	// ingest's --attribute-by-window.
	id, source := resolve(req.Context())
	caller := callerOf(req.Context(), o.caller)
	service := o.service
	if service == "" {
		service = hostName(req.URL.Host)
	}

	// Never mutate the caller's request (the RoundTripper contract): work on a clone.
	out := req.Clone(context.WithValue(req.Context(), capturedKey{}, true))
	var reqBody []byte
	if req.Body != nil && req.Body != http.NoBody {
		b, err := io.ReadAll(req.Body)
		req.Body.Close()
		if err != nil {
			return nil, err
		}
		reqBody = b
		out.Body = io.NopCloser(bytes.NewReader(b))
		out.GetBody = func() (io.ReadCloser, error) { return io.NopCloser(bytes.NewReader(b)), nil }
	}
	traceID := out.Header.Get(HeaderTraceID)
	if traceID == "" {
		traceID = newGUID()
	}
	span := randHex(8)
	if id.ID != "" {
		out.Header.Set(HeaderTestName, id.Name)
		out.Header.Set(HeaderTestID, id.ID)
		out.Header.Set(HeaderCaller, caller)
		out.Header.Set(HeaderTraceID, traceID)
		if out.Header.Get("traceparent") == "" {
			out.Header.Set("traceparent", "00-"+id.ID+"-"+span+"-01")
		}
		out.Header.Set("baggage", "kronikol.test-id="+id.ID) // foundations L2: the second carrier
	}

	rrid := newGUID()
	base := Interaction{
		Method: req.Method, URI: req.URL.String(), ServiceName: service, CallerName: caller,
		TraceID: traceID, RequestResponseID: rrid, TestID: id.ID, TestName: id.Name,
		DependencyCategory: o.category, ActivityTraceID: id.ID, ActivitySpanID: span,
		AttributionSource: source,
	}
	sent := time.Now()
	reqRec := base
	reqRec.Type, reqRec.Content, reqRec.Headers, reqRec.Timestamp = "Request", string(reqBody), headers(out.Header), now()
	writeInteraction(reqRec)

	resp, err := next.RoundTrip(out)
	respRec := base
	respRec.Type = "Response"
	if err != nil {
		respRec.StatusCode, respRec.Content, respRec.Timestamp = "Error", err.Error(), now()
		respRec.DurationMs = ms(time.Since(sent))
		writeInteraction(respRec)
		return nil, err
	}
	respRec.StatusCode, respRec.Headers = strconv.Itoa(resp.StatusCode), headers(resp.Header)

	// H3: a body of known length within the cap is read before returning, which completes the
	// record at once; anything else (chunked, SSE, large) is teed, so the caller's timing is untouched.
	if resp.ContentLength >= 0 && resp.ContentLength <= bodyCap {
		b, rerr := io.ReadAll(resp.Body)
		resp.Body.Close()
		resp.Body = io.NopCloser(bytes.NewReader(b))
		if rerr != nil {
			respRec.Content = "[kronikol: body read failed: " + rerr.Error() + "]"
		} else {
			respRec.Content = string(b)
		}
		respRec.Timestamp, respRec.DurationMs = now(), ms(time.Since(sent))
		writeInteraction(respRec)
		return resp, nil
	}
	tb := &teeBody{rc: resp.Body, rec: respRec, sent: sent}
	pending.add(id.ID, tb)
	resp.Body = tb
	return resp, nil
}

func ms(d time.Duration) float64 { return float64(d.Microseconds()) / 1000 }

// Security redaction happens at capture (foundations §4.4): a secret in the NDJSON has leaked.
func headers(h http.Header) []Header {
	var out []Header
	redacted := false
	for k, vs := range h {
		for _, v := range vs {
			switch strings.ToLower(k) {
			case "authorization", "cookie", "set-cookie":
				v, redacted = "[REDACTED]", true
			}
			out = append(out, Header{Key: k, Value: v})
		}
	}
	if redacted {
		out = append(out, Header{Key: "x-kronikol-redacted", Value: "true"})
	}
	return out
}

type teeBody struct {
	rc   io.ReadCloser
	rec  Interaction
	sent time.Time
	buf  bytes.Buffer
	once sync.Once
	cut  bool
}

func (t *teeBody) Read(p []byte) (int, error) {
	n, err := t.rc.Read(p)
	if room := bodyCap - t.buf.Len(); room > 0 {
		t.buf.Write(p[:min(n, room)])
	} else if n > 0 {
		t.cut = true
	}
	if err == io.EOF {
		t.finish("")
	}
	return n, err
}

func (t *teeBody) Close() error { t.finish(""); return t.rc.Close() }

func (t *teeBody) finish(note string) {
	t.once.Do(func() {
		t.rec.Content = t.buf.String()
		if t.cut {
			t.rec.Content += "\n[kronikol: capped at 64 KiB]"
		}
		t.rec.Content += note
		t.rec.Timestamp, t.rec.DurationMs = now(), ms(time.Since(t.sent))
		writeInteraction(t.rec)
		pending.remove(t)
	})
}

// pending holds teed responses whose body nobody has finished reading; a scenario's end flushes its own.
var pending = &pendingSet{m: map[*teeBody]string{}}

type pendingSet struct {
	mu sync.Mutex
	m  map[*teeBody]string
}

func (p *pendingSet) add(testID string, t *teeBody) { p.mu.Lock(); p.m[t] = testID; p.mu.Unlock() }
func (p *pendingSet) remove(t *teeBody)             { p.mu.Lock(); delete(p.m, t); p.mu.Unlock() }

func (p *pendingSet) flush(testID string) {
	p.mu.Lock()
	var mine []*teeBody
	for t, id := range p.m {
		if id == testID {
			mine = append(mine, t)
		}
	}
	p.mu.Unlock()
	for _, t := range mine {
		t.finish("\n[kronikol: the caller had not finished reading this body when the test ended]")
	}
}

// Handler is the server-side half: it reads the identity headers into the request's context (only
// when both name and id are present, NODE_PORT_PLAN §7's topology-4 rule) and marks the context as
// served by service, so the calls the handler makes name it as their caller.
func Handler(next http.Handler, opts ...Option) http.Handler {
	o := build(opts)
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		ctx := r.Context()
		name, id := r.Header.Get(HeaderTestName), r.Header.Get(HeaderTestID)
		if o.service != "" {
			ctx = context.WithValue(ctx, sutKey{}, o.service)
		}
		if name != "" && id != "" {
			who := Identity{ID: id, Name: name}
			ctx = context.WithValue(ctx, identityKey{}, scoped{who, SourceRequestHeader})
			label(ctx, who) // re-label: a server started inside another test carries that test (H5)
		}
		next.ServeHTTP(w, r.WithContext(ctx))
	})
}

// InstrumentDefaultTransport records every plain-http request that goes through the default
// transport, including clients built before this call, without changing its type (H2). https stays
// uncaptured: HTTP/2 has already registered that scheme.
func InstrumentDefaultTransport(opts ...Option) error {
	dt, ok := http.DefaultTransport.(*http.Transport)
	if !ok {
		return nil
	}
	inner := &http.Transport{Proxy: dt.Proxy, DialContext: dt.DialContext, MaxIdleConns: dt.MaxIdleConns,
		IdleConnTimeout: dt.IdleConnTimeout, TLSHandshakeTimeout: dt.TLSHandshakeTimeout,
		ExpectContinueTimeout: dt.ExpectContinueTimeout}
	o := build(opts)
	dt.RegisterProtocol("http", roundTripFunc(func(req *http.Request) (*http.Response, error) {
		if req.Context().Value(capturedKey{}) != nil {
			return inner.RoundTrip(req) // a wrapped client already recorded it
		}
		return roundTrip(inner, o, req)
	}))
	return nil
}

var hostNames sync.Map

// NameHost gives the participant name for requests to host:port, for the global hook, which has no
// wrap site to name the service at.
func NameHost(hostPort, name string) { hostNames.Store(hostPort, name) }

func hostName(hostPort string) string {
	if n, ok := hostNames.Load(hostPort); ok {
		return n.(string)
	}
	return hostPort
}

type roundTripFunc func(*http.Request) (*http.Response, error)

func (f roundTripFunc) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }
