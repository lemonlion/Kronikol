package orders_test

import (
	"context"
	"database/sql/driver"
	"io"
)

// fakeDB stands in for PostgreSQL: a driver.Connector whose connections implement the context
// interfaces, as lib/pq and pgx's stdlib adapter do.
type fakeDB struct{}

func (fakeDB) Connect(context.Context) (driver.Conn, error) { return conn{}, nil }
func (fakeDB) Driver() driver.Driver                        { return drv{} }

type drv struct{}

func (drv) Open(string) (driver.Conn, error) { return conn{}, nil }

type conn struct{}

func (conn) Prepare(string) (driver.Stmt, error) { return nil, driver.ErrSkip }
func (conn) Close() error                        { return nil }
func (conn) Begin() (driver.Tx, error)           { return nil, driver.ErrSkip }
func (conn) QueryContext(_ context.Context, _ string, args []driver.NamedValue) (driver.Rows, error) {
	return &rows{sku: args[0].Value.(string)}, nil
}
func (conn) ExecContext(context.Context, string, []driver.NamedValue) (driver.Result, error) {
	return driver.RowsAffected(1), nil
}

type rows struct {
	sku  string
	done bool
}

func (r *rows) Columns() []string { return []string{"price"} }
func (r *rows) Close() error      { return nil }
func (r *rows) Next(dest []driver.Value) error {
	if r.done {
		return io.EOF
	}
	r.done = true
	dest[0] = int64(len(r.sku) * 100)
	return nil
}
