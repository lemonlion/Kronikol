//go:build go1.27

// H9b. Three testing additions a Go capturer would use or must survive: T.Attr in the -json stream,
// T.ArtifactDir under -artifacts, and httptest.NewTestServer's in-memory network.
package h9

import (
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
)

func TestAttrAndArtifacts(t *testing.T) {
	t.Attr("kronikol.testId", "4bf92f3577b34da6a3ce929d0e0e4736")
	dir := t.ArtifactDir()
	os.WriteFile(filepath.Join(dir, "interactions.ndjson"), []byte("{}\n"), 0o644)
	t.Logf("ArtifactDir = %s", dir)
	t.Run("sub case", func(t *testing.T) { t.Logf("subtest ArtifactDir = %s", t.ArtifactDir()) })
}

func TestInMemoryServer(t *testing.T) {
	srv := httptest.NewTestServer(t, http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { io.WriteString(w, "ok") }))
	t.Logf("URL = %s, srv.Client().Transport is %T", srv.URL, srv.Client().Transport)
	if resp, err := srv.Client().Get(srv.URL); err == nil {
		b, _ := io.ReadAll(resp.Body)
		resp.Body.Close()
		t.Logf("srv.Client().Get: %q", b)
	} else {
		t.Logf("srv.Client().Get: err = %v", err)
	}
	if resp, err := http.Get(srv.URL); err == nil {
		b, _ := io.ReadAll(resp.Body)
		resp.Body.Close()
		t.Logf("http.Get(srv.URL) through the default transport: status %d, body starts %q", resp.StatusCode, string(b[:min(len(b), 40)]))
	} else {
		t.Logf("http.Get(srv.URL) through the default transport: err = %v", err)
	}
}
