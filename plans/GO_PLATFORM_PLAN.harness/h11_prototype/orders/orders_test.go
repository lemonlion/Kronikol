// A component test in the usual Go shape, instrumented with the prototype. The wiring is the whole
// cost to the user: TestMain, one Start per test, and the wrapped transports and connector where the
// test already builds the service.
package orders_test

import (
	"context"
	"database/sql"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"strings"
	"testing"

	"h11/kgo"
	"h11/orders"
)

func TestMain(m *testing.M) {
	kgo.InstrumentDefaultTransport() // catches the service's context-less http.Get
	os.Exit(kgo.Main(m))
}

func newSystem(t *testing.T) (*http.Client, string) {
	payments := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/charge":
			io.WriteString(w, `{"status":"approved"}`)
		default:
			w.WriteHeader(http.StatusNoContent)
		}
	}))
	t.Cleanup(payments.Close)
	kgo.NameHost(payments.Listener.Addr().String(), "Payments")

	svc := &orders.Service{
		DB:       sql.OpenDB(kgo.Connector(fakeDB{}, kgo.Service("orders-db"), kgo.Category("PostgreSQL"))),
		Payments: payments.URL,
		Client:   &http.Client{Transport: kgo.Transport(http.DefaultTransport, kgo.Service("Payments"))},
	}
	api := httptest.NewServer(kgo.Handler(svc.Router(), kgo.Service("Orders API")))
	t.Cleanup(api.Close)
	return &http.Client{Transport: kgo.Transport(api.Client().Transport, kgo.Service("Orders API"))}, api.URL
}

func post(ctx context.Context, t *testing.T, c *http.Client, url, body string) int {
	t.Helper()
	req, _ := http.NewRequestWithContext(ctx, "POST", url+"/orders", strings.NewReader(body))
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer secret-token")
	resp, err := c.Do(req)
	if err != nil {
		t.Fatal(err)
	}
	resp.Body.Close()
	return resp.StatusCode
}

func TestPlaceOrder(t *testing.T) {
	ctx := kgo.Start(t)
	client, url := newSystem(t)
	var status int
	kgo.Step(ctx, "Given", "a customer with a basket of two widgets", func(context.Context) {})
	kgo.Step(ctx, "When", "the order is placed", func(ctx context.Context) {
		status = post(ctx, t, client, url, `{"sku":"WIDGET","qty":2}`)
	})
	kgo.Step(ctx, "Then", "the order is created", func(context.Context) {
		if status != http.StatusCreated {
			t.Errorf("status = %d, want %d", status, http.StatusCreated)
		}
	})
}

func TestOrderValidation(t *testing.T) {
	for _, tc := range []struct {
		name, body string
		want       int
	}{
		{"valid order", `{"sku":"GADGET","qty":1}`, http.StatusCreated},
		{"zero quantity", `{"sku":"GADGET","qty":0}`, http.StatusBadRequest},
	} {
		t.Run(tc.name, func(t *testing.T) {
			ctx := kgo.Start(t)
			client, url := newSystem(t)
			if got := post(ctx, t, client, url, tc.body); got != tc.want {
				t.Errorf("status = %d, want %d", got, tc.want)
			}
		})
	}
}

func TestParallelCustomers(t *testing.T) {
	for _, sku := range []string{"ALICE-1", "BOB-22"} {
		t.Run(sku, func(t *testing.T) {
			t.Parallel()
			ctx := kgo.Start(t)
			client, url := newSystem(t)
			post(ctx, t, client, url, `{"sku":"`+sku+`","qty":1}`)
		})
	}
}

// Fails on purpose, in the Go idiom, so the report has a failure whose text only go test's own
// stream carries: testing.T has no method that returns what t.Errorf logged.
func TestRefundIsIssued(t *testing.T) {
	ctx := kgo.Start(t)
	client, url := newSystem(t)
	if got, want := post(ctx, t, client, url, `{"sku":"WIDGET","qty":1}`), http.StatusOK; got != want {
		t.Errorf("POST /orders = %d, want %d", got, want)
	}
}
