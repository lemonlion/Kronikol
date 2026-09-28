package kgo

import (
	"context"
	"database/sql/driver"
	"encoding/json"
	"fmt"
	"strings"
	"time"
)

// Connector wraps a database/sql connector (H6): every statement, its argument values and the first
// ten rows it returned become a request/response pair, attributed through the context the caller
// passed to ExecContext or QueryContext. Use it with sql.OpenDB.
//
// Prototype limit, measured in H6: the rows wrapper forwards only driver.Rows, so a driver's optional
// Rows interfaces (ColumnTypeDatabaseTypeName, NextResultSet, RowsColumnScanner in Go 1.27) vanish
// behind it. The plan's §3.4 requires every optional interface to be forwarded.
func Connector(c driver.Connector, opts ...Option) driver.Connector {
	o := build(opts)
	if o.category == "" {
		o.category = "SQL"
	}
	return &connector{inner: c, o: o}
}

type connector struct {
	inner driver.Connector
	o     options
}

func (c *connector) Connect(ctx context.Context) (driver.Conn, error) {
	conn, err := c.inner.Connect(ctx)
	if err != nil {
		return nil, err
	}
	return &conn_{inner: conn, o: c.o}, nil
}

func (c *connector) Driver() driver.Driver { return c.inner.Driver() }

type conn_ struct {
	inner driver.Conn
	o     options
}

func (c *conn_) Prepare(q string) (driver.Stmt, error) {
	return c.PrepareContext(context.Background(), q)
}
func (c *conn_) Close() error              { return c.inner.Close() }
func (c *conn_) Begin() (driver.Tx, error) { return c.inner.Begin() } //nolint:staticcheck

func (c *conn_) PrepareContext(ctx context.Context, q string) (driver.Stmt, error) {
	var s driver.Stmt
	var err error
	if p, ok := c.inner.(driver.ConnPrepareContext); ok {
		s, err = p.PrepareContext(ctx, q)
	} else {
		s, err = c.inner.Prepare(q)
	}
	if err != nil {
		return nil, err
	}
	return &stmt{inner: s, q: q, o: c.o}, nil
}

func (c *conn_) QueryContext(ctx context.Context, q string, args []driver.NamedValue) (driver.Rows, error) {
	qc, ok := c.inner.(driver.QueryerContext)
	if !ok {
		return nil, driver.ErrSkip // database/sql prepares instead, and stmt records it
	}
	call := begin(ctx, c.o, q, args)
	rows, err := qc.QueryContext(ctx, q, args)
	if err != nil {
		call.end("", err)
		return nil, err
	}
	return &rows_{inner: rows, call: call}, nil
}

func (c *conn_) ExecContext(ctx context.Context, q string, args []driver.NamedValue) (driver.Result, error) {
	ec, ok := c.inner.(driver.ExecerContext)
	if !ok {
		return nil, driver.ErrSkip
	}
	call := begin(ctx, c.o, q, args)
	res, err := ec.ExecContext(ctx, q, args)
	call.endResult(res, err)
	return res, err
}

func (c *conn_) BeginTx(ctx context.Context, opts driver.TxOptions) (driver.Tx, error) {
	if b, ok := c.inner.(driver.ConnBeginTx); ok {
		return b.BeginTx(ctx, opts)
	}
	return c.inner.Begin() //nolint:staticcheck
}

type stmt struct {
	inner driver.Stmt
	q     string
	o     options
}

func (s *stmt) Close() error                                 { return s.inner.Close() }
func (s *stmt) NumInput() int                                { return s.inner.NumInput() }
func (s *stmt) Exec(a []driver.Value) (driver.Result, error) { return s.inner.Exec(a) }  //nolint:staticcheck
func (s *stmt) Query(a []driver.Value) (driver.Rows, error)  { return s.inner.Query(a) } //nolint:staticcheck

func (s *stmt) ExecContext(ctx context.Context, args []driver.NamedValue) (driver.Result, error) {
	call := begin(ctx, s.o, s.q, args)
	var res driver.Result
	var err error
	if e, ok := s.inner.(driver.StmtExecContext); ok {
		res, err = e.ExecContext(ctx, args)
	} else {
		res, err = s.inner.Exec(values(args)) //nolint:staticcheck
	}
	call.endResult(res, err)
	return res, err
}

func (s *stmt) QueryContext(ctx context.Context, args []driver.NamedValue) (driver.Rows, error) {
	call := begin(ctx, s.o, s.q, args)
	var rows driver.Rows
	var err error
	if q, ok := s.inner.(driver.StmtQueryContext); ok {
		rows, err = q.QueryContext(ctx, args)
	} else {
		rows, err = s.inner.Query(values(args)) //nolint:staticcheck
	}
	if err != nil {
		call.end("", err)
		return nil, err
	}
	return &rows_{inner: rows, call: call}, nil
}

func values(nv []driver.NamedValue) []driver.Value {
	v := make([]driver.Value, len(nv))
	for i := range nv {
		v[i] = nv[i].Value
	}
	return v
}

type rows_ struct {
	inner driver.Rows
	call  *sqlCall
	kept  []map[string]any
	n     int
}

func (r *rows_) Columns() []string { return r.inner.Columns() }

func (r *rows_) Next(dest []driver.Value) error {
	err := r.inner.Next(dest)
	if err == nil {
		r.n++
		if len(r.kept) < 10 { // MaxResponseRows' default on .NET
			row := map[string]any{}
			for i, c := range r.inner.Columns() {
				row[c] = dest[i]
			}
			r.kept = append(r.kept, row)
		}
	}
	return err
}

func (r *rows_) Close() error {
	b, _ := json.Marshal(r.kept)
	r.call.end(fmt.Sprintf("%d rows\n%s", r.n, b), nil)
	return r.inner.Close()
}

// sqlCall is one statement in flight. The identity is read once, when the statement starts
// (NODE_PORT_PLAN §3.2's bind-at-call-site rule), and reused for the response.
type sqlCall struct {
	base  Interaction
	start time.Time
	done  bool
}

func begin(ctx context.Context, o options, q string, args []driver.NamedValue) *sqlCall {
	id, source := resolve(ctx)
	caller := callerOf(ctx, o.caller)
	scheme := strings.ToLower(o.category)
	rrid := newGUID()
	base := Interaction{ServiceName: o.service, CallerName: caller, TraceID: rrid, RequestResponseID: rrid,
		TestID: id.ID, TestName: id.Name, DependencyCategory: o.category, AttributionSource: source}
	req := base
	req.Type, req.Timestamp = "Request", now()
	// Pre-F3 the capturer must supply the arrow's label itself; after F3 it sends the raw statement
	// and a dialect hint, and the shared classifier decides (foundations §3, F3).
	req.Method = strings.ToUpper(strings.Fields(q + " SQL")[0])
	req.URI = scheme + ":///" + o.service
	req.Content = q
	if len(args) > 0 {
		vals := make([]string, len(args))
		for i, a := range args {
			vals[i] = fmt.Sprintf("$%d = %v", i+1, a.Value)
		}
		req.Content += "\n-- " + strings.Join(vals, ", ")
	}
	writeInteraction(req)
	return &sqlCall{base: base, start: time.Now()}
}

func (c *sqlCall) end(content string, err error) {
	if c.done {
		return
	}
	c.done = true
	resp := c.base
	resp.Type, resp.URI, resp.Timestamp = "Response", strings.SplitN(c.base.URI, ":", 2)[0]+":///", now()
	resp.DurationMs = ms(time.Since(c.start))
	resp.StatusCode, resp.Content = "OK", content
	if err != nil {
		resp.StatusCode, resp.Content = "Error", err.Error()
	}
	writeInteraction(resp)
}

func (c *sqlCall) endResult(res driver.Result, err error) {
	if err != nil {
		c.end("", err)
		return
	}
	n, _ := res.RowsAffected()
	c.end(fmt.Sprintf("%d rows affected", n), nil)
}
