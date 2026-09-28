// Package orders is the system under test: a small HTTP service with a database and a payments
// dependency, written the way Go services usually are (constructor injection, context passed down).
// Two calls deliberately drop the context, the case the plan's attribution cascade exists for.
package orders

import (
	"context"
	"database/sql"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"strings"
)

type Service struct {
	DB       *sql.DB
	Payments string       // base URL
	Client   *http.Client // the service's own client for payments
}

func (s *Service) Router() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("POST /orders", s.place)
	return mux
}

func (s *Service) place(w http.ResponseWriter, r *http.Request) {
	ctx := r.Context()
	var in struct {
		SKU string `json:"sku"`
		Qty int    `json:"qty"`
	}
	if err := json.NewDecoder(r.Body).Decode(&in); err != nil || in.Qty <= 0 {
		http.Error(w, `{"error":"quantity must be positive"}`, http.StatusBadRequest)
		return
	}
	var price int
	if err := s.DB.QueryRowContext(ctx, "SELECT price FROM products WHERE sku = $1", in.SKU).Scan(&price); err != nil {
		http.Error(w, err.Error(), http.StatusInternalServerError)
		return
	}
	body := fmt.Sprintf(`{"amount":%d}`, price*in.Qty)
	req, _ := http.NewRequestWithContext(ctx, "POST", s.Payments+"/charge", strings.NewReader(body))
	req.Header.Set("Content-Type", "application/json")
	resp, err := s.Client.Do(req)
	if err != nil {
		http.Error(w, err.Error(), http.StatusBadGateway)
		return
	}
	io.Copy(io.Discard, resp.Body)
	resp.Body.Close()
	s.DB.ExecContext(ctx, "INSERT INTO orders (sku, qty, total) VALUES ($1, $2, $3)", in.SKU, in.Qty, price*in.Qty)

	// Legacy: an audit ping through the default client, with no context at all.
	if resp, err := http.Get(s.Payments + "/audit?sku=" + in.SKU); err == nil {
		resp.Body.Close()
	}
	// A goroutine that starts from context.Background(): the identity can only reach it by inheritance.
	done := make(chan struct{})
	go func() {
		defer close(done)
		req, _ := http.NewRequestWithContext(context.Background(), "POST", s.Payments+"/notify", strings.NewReader(`{"sku":"`+in.SKU+`"}`))
		if resp, err := s.Client.Do(req); err == nil {
			resp.Body.Close()
		}
	}()
	<-done

	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusCreated)
	fmt.Fprintf(w, `{"sku":%q,"qty":%d,"total":%d}`, in.SKU, in.Qty, price*in.Qty)
}
