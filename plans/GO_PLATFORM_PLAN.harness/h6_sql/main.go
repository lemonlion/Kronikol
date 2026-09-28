// H6. database/sql as Go's JDBC: one wrapper around driver.Connector sees statement text, argument
// values, the caller's context.Context (so the test identity) and result rows, for every driver that
// implements database/sql's interfaces. Two fake drivers bound the space: "min" implements only the
// mandatory driver.Conn/Stmt/Rows, "full" implements every optional context interface plus two
// optional Rows interfaces. The last section shows the silent hazard a wrapper carries: an optional
// interface it does not forward disappears from the caller's view with no error.
package main

import (
	"context"
	"database/sql"
	"database/sql/driver"
	"fmt"
	"io"
	"strings"
)

// ---------------------------------------------------------------- fake drivers

type fakeDriver struct{ full bool }

func (d fakeDriver) Open(string) (driver.Conn, error) {
	if d.full {
		return &fullConn{}, nil
	}
	return &minConn{}, nil
}

type minConn struct{}

func (c *minConn) Prepare(q string) (driver.Stmt, error) { return &minStmt{q: q}, nil }
func (c *minConn) Close() error                          { return nil }
func (c *minConn) Begin() (driver.Tx, error)             { return fakeTx{}, nil }

type minStmt struct{ q string }

func (s *minStmt) Close() error  { return nil }
func (s *minStmt) NumInput() int { return -1 }
func (s *minStmt) Exec(args []driver.Value) (driver.Result, error) {
	return driver.RowsAffected(1), nil
}
func (s *minStmt) Query(args []driver.Value) (driver.Rows, error) { return &fakeRows{}, nil }

type fullConn struct{ minConn }

func (c *fullConn) PrepareContext(_ context.Context, q string) (driver.Stmt, error) {
	return &minStmt{q: q}, nil
}
func (c *fullConn) QueryContext(context.Context, string, []driver.NamedValue) (driver.Rows, error) {
	return &fullRows{}, nil
}
func (c *fullConn) ExecContext(context.Context, string, []driver.NamedValue) (driver.Result, error) {
	return driver.RowsAffected(1), nil
}
func (c *fullConn) BeginTx(context.Context, driver.TxOptions) (driver.Tx, error) {
	return fakeTx{}, nil
}

type fakeTx struct{}

func (fakeTx) Commit() error   { return nil }
func (fakeTx) Rollback() error { return nil }

type fakeRows struct{ i int }

func (r *fakeRows) Columns() []string { return []string{"id", "sku"} }
func (r *fakeRows) Close() error      { return nil }
func (r *fakeRows) Next(dest []driver.Value) error {
	if r.i == 3 {
		return io.EOF
	}
	r.i++
	dest[0], dest[1] = int64(r.i), fmt.Sprintf("SKU-%d", r.i)
	return nil
}

// fullRows adds two optional Rows interfaces real drivers implement (lib/pq, pgx's stdlib, go-mssqldb).
type fullRows struct{ fakeRows }

func (r *fullRows) ColumnTypeDatabaseTypeName(i int) string { return []string{"INT8", "TEXT"}[i] }
func (r *fullRows) HasNextResultSet() bool                  { return true }
func (r *fullRows) NextResultSet() error                    { r.i = 0; return nil }

// ---------------------------------------------------------------- the wrapper

type ctxKey struct{}

func testID(ctx context.Context) string {
	if id, ok := ctx.Value(ctxKey{}).(string); ok {
		return id
	}
	return "<none>"
}

var log []string

func record(ctx context.Context, path, query string, args []driver.NamedValue) {
	vals := make([]string, len(args))
	for i, a := range args {
		vals[i] = fmt.Sprint(a.Value)
	}
	log = append(log, fmt.Sprintf("%-16s test=%-6s %q args=[%s]", path, testID(ctx), query, strings.Join(vals, ", ")))
}

type connector struct{ d driver.Driver }

func (c connector) Connect(context.Context) (driver.Conn, error) {
	inner, err := c.d.Open("")
	return &wConn{inner: inner}, err
}
func (c connector) Driver() driver.Driver { return c.d }

// wConn implements every optional interface and answers driver.ErrSkip where the inner connection
// lacks one, so database/sql takes the path it would have taken without the wrapper.
type wConn struct{ inner driver.Conn }

func (c *wConn) Prepare(q string) (driver.Stmt, error) {
	return c.PrepareContext(context.Background(), q)
}
func (c *wConn) Close() error              { return c.inner.Close() }
func (c *wConn) Begin() (driver.Tx, error) { return c.inner.Begin() } //nolint:staticcheck

func (c *wConn) PrepareContext(ctx context.Context, q string) (driver.Stmt, error) {
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
	return &wStmt{inner: s, q: q}, nil
}

func (c *wConn) QueryContext(ctx context.Context, q string, args []driver.NamedValue) (driver.Rows, error) {
	qc, ok := c.inner.(driver.QueryerContext)
	if !ok {
		return nil, driver.ErrSkip
	}
	record(ctx, "conn.Query", q, args)
	rows, err := qc.QueryContext(ctx, q, args)
	if err != nil {
		return nil, err
	}
	return &wRows{inner: rows}, nil
}

func (c *wConn) ExecContext(ctx context.Context, q string, args []driver.NamedValue) (driver.Result, error) {
	ec, ok := c.inner.(driver.ExecerContext)
	if !ok {
		return nil, driver.ErrSkip
	}
	record(ctx, "conn.Exec", q, args)
	return ec.ExecContext(ctx, q, args)
}

func (c *wConn) BeginTx(ctx context.Context, o driver.TxOptions) (driver.Tx, error) {
	record(ctx, "conn.BeginTx", "BEGIN", nil)
	if b, ok := c.inner.(driver.ConnBeginTx); ok {
		return b.BeginTx(ctx, o)
	}
	return c.inner.Begin() //nolint:staticcheck
}

type wStmt struct {
	inner driver.Stmt
	q     string
}

func (s *wStmt) Close() error  { return s.inner.Close() }
func (s *wStmt) NumInput() int { return s.inner.NumInput() }
func (s *wStmt) Exec(a []driver.Value) (driver.Result, error) {
	return s.inner.Exec(a) //nolint:staticcheck
}
func (s *wStmt) Query(a []driver.Value) (driver.Rows, error) {
	return s.inner.Query(a) //nolint:staticcheck
}

func (s *wStmt) ExecContext(ctx context.Context, args []driver.NamedValue) (driver.Result, error) {
	record(ctx, "stmt.Exec", s.q, args)
	if e, ok := s.inner.(driver.StmtExecContext); ok {
		return e.ExecContext(ctx, args)
	}
	return s.inner.Exec(values(args)) //nolint:staticcheck
}

func (s *wStmt) QueryContext(ctx context.Context, args []driver.NamedValue) (driver.Rows, error) {
	record(ctx, "stmt.Query", s.q, args)
	var rows driver.Rows
	var err error
	if q, ok := s.inner.(driver.StmtQueryContext); ok {
		rows, err = q.QueryContext(ctx, args)
	} else {
		rows, err = s.inner.Query(values(args)) //nolint:staticcheck
	}
	if err != nil {
		return nil, err
	}
	return &wRows{inner: rows}, nil
}

func values(nv []driver.NamedValue) []driver.Value {
	v := make([]driver.Value, len(nv))
	for i := range nv {
		v[i] = nv[i].Value
	}
	return v
}

// wRows keeps the first rows it sees for the record, and forwards only the mandatory methods: the
// naive shape. It is what the last section measures.
type wRows struct {
	inner driver.Rows
	kept  [][]driver.Value
}

func (r *wRows) Columns() []string { return r.inner.Columns() }
func (r *wRows) Close() error {
	log = append(log, fmt.Sprintf("%-16s rows kept for the record: %v", "rows.Close", r.kept))
	return r.inner.Close()
}
func (r *wRows) Next(dest []driver.Value) error {
	err := r.inner.Next(dest)
	if err == nil && len(r.kept) < 10 {
		r.kept = append(r.kept, append([]driver.Value(nil), dest...))
	}
	return err
}

// ---------------------------------------------------------------- the probe

func main() {
	sql.Register("fake-min", fakeDriver{})
	sql.Register("fake-full", fakeDriver{full: true})
	func() {
		defer func() { fmt.Printf("H6 sql.Register(\"fake-min\", wrapper) a second time: panic = %v\n", recover()) }()
		sql.Register("fake-min", fakeDriver{})
	}()

	ctx := context.WithValue(context.Background(), ctxKey{}, "T1")
	for _, full := range []bool{false, true} {
		name := map[bool]string{false: "min", true: "full"}[full]
		db := sql.OpenDB(connector{d: fakeDriver{full: full}})
		log = nil
		db.ExecContext(ctx, "INSERT INTO orders(sku, qty) VALUES ($1, $2)", "SKU-9", 2)
		rows, _ := db.QueryContext(ctx, "SELECT id, sku FROM orders WHERE qty > $1", 1)
		for rows.Next() {
		}
		rows.Close()
		var id int64
		var sku string
		db.QueryRowContext(ctx, "SELECT id, sku FROM orders WHERE id = $1", 7).Scan(&id, &sku)
		st, _ := db.PrepareContext(ctx, "UPDATE orders SET qty = $1 WHERE id = $2")
		st.ExecContext(context.WithValue(ctx, ctxKey{}, "T2"), 5, 7)
		st.Close()
		tx, _ := db.BeginTx(ctx, nil)
		tx.ExecContext(ctx, "DELETE FROM orders WHERE id = $1", 7)
		tx.Commit()
		db.Exec("SELECT 1 /* no context */")
		fmt.Printf("H6 driver=%s: what the wrapper saw\n", name)
		for _, l := range log {
			fmt.Println("  " + l)
		}
		db.Close()
	}

	fmt.Println("H6 optional Rows interfaces through the naive wrapper (driver=full):")
	for _, wrapped := range []bool{false, true} {
		var db *sql.DB
		if wrapped {
			db = sql.OpenDB(connector{d: fakeDriver{full: true}})
		} else {
			db, _ = sql.Open("fake-full", "")
		}
		rows, err := db.QueryContext(ctx, "EXEC two_result_sets")
		if err != nil {
			fmt.Println("  error:", err)
			continue
		}
		types, _ := rows.ColumnTypes()
		for rows.Next() {
		}
		fmt.Printf("  wrapped=%-5v DatabaseTypeName(col 0)=%-6q NextResultSet()=%v\n",
			wrapped, types[0].DatabaseTypeName(), rows.NextResultSet())
		rows.Close()
		db.Close()
	}
}
