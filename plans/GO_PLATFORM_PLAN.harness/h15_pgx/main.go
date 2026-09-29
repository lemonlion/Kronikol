// H15. pgx's own interface on a real PostgreSQL: what each of its tracers hands a capturer, which calls
// no tracer sees, and whether a tap on the connection's plaintext recovers what the tracer does not
// carry (result rows, COPY rows), attributed to the right test through the tracer's context.
//
// The tap sits in pgconn's AfterNetConnect (pgx 5.8+), which hands over the net.Conn after TLS; the
// BuildFrontend variant is the route for older pgx. A DialFunc wrapper is shown for contrast: it sits
// below TLS. The probe decodes the wire with pgx's own pgproto3 and pgtype, so it adds no dependency.
//
// PGURL names the database. The probe creates, fills and drops one table, h15_orders, and needs the
// server to accept both sslmode=disable and sslmode=require: point it at a throwaway database.
package main

import (
	"context"
	"crypto/tls"
	"database/sql/driver"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"runtime/debug"
	"strings"
	"sync"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
	"github.com/jackc/pgx/v5/pgproto3"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/jackc/pgx/v5/pgxpool"
)

// ---------------------------------------------------------------- identity, as kronikol.Start(t) would carry it

type testKey struct{}

func withTest(ctx context.Context, id string) context.Context {
	return context.WithValue(ctx, testKey{}, id)
}

func testOf(ctx context.Context) string {
	if id, ok := ctx.Value(testKey{}).(string); ok {
		return id
	}
	return "(none)"
}

// ---------------------------------------------------------------- the tap: a PostgreSQL protocol reader

type field struct {
	name   string
	oid    uint32
	format int16
}

// result is one statement's outcome as the wire carried it.
type result struct {
	fields []field
	from   string
	rows   [][][]byte
	tag    string
	err    string
}

// queued is a request whose answer is still to come, tagged with the Sync segment it was sent in so that
// requests the server skipped after an error are dropped at the next ReadyForQuery.
type queued struct {
	seg     int
	name    string
	formats []int16
}

type tap struct {
	mu        sync.Mutex
	tx, rx    []byte
	started   bool
	txZ, rxZ  int                // requests answered by ReadyForQuery (startup, Sync, Query), and answers seen
	stmts     map[string][]field // prepared statement -> its columns, from Describe
	stmtSQL   map[string]string  // prepared statement -> its SQL, from Parse
	describes []queued
	binds     []queued
	stmtDesc  bool // a ParameterDescription arrived: the next RowDescription or NoData describes a statement
	cur       *result
	done      []result
	copyData  []byte
	holder    string // the test holding the connection, from the pool's acquire tracer
}

func newTap() *tap { return &tap{stmts: map[string][]field{}, stmtSQL: map[string]string{}} }

func frame(buf *[]byte) (byte, []byte, bool) {
	b := *buf
	if len(b) < 5 {
		return 0, nil, false
	}
	n := int(binary.BigEndian.Uint32(b[1:5]))
	if len(b) < 1+n {
		return 0, nil, false
	}
	body := append([]byte(nil), b[5:1+n]...)
	*buf = b[1+n:]
	return b[0], body, true
}

// sent reads what the client wrote.
func (t *tap) sent(p []byte) {
	t.mu.Lock()
	defer t.mu.Unlock()
	t.tx = append(t.tx, p...)
	for {
		if !t.started { // the StartupMessage has a length and no type byte
			if len(t.tx) < 4 || len(t.tx) < int(binary.BigEndian.Uint32(t.tx)) {
				return
			}
			t.tx, t.started, t.txZ = t.tx[binary.BigEndian.Uint32(t.tx):], true, 1
			continue
		}
		typ, body, ok := frame(&t.tx)
		if !ok {
			return
		}
		switch typ {
		case 'P':
			var m pgproto3.Parse
			if m.Decode(body) == nil {
				t.stmtSQL[m.Name] = m.Query
			}
		case 'D':
			var m pgproto3.Describe
			if m.Decode(body) == nil && m.ObjectType == 'S' {
				t.describes = append(t.describes, queued{seg: t.txZ, name: m.Name})
			}
		case 'B':
			var m pgproto3.Bind
			if m.Decode(body) == nil {
				t.binds = append(t.binds, queued{seg: t.txZ, name: m.PreparedStatement, formats: m.ResultFormatCodes})
			}
		case 'S', 'Q':
			t.txZ++
		case 'd':
			t.copyData = append(t.copyData, body...)
		}
	}
}

func (t *tap) pop(q *[]queued) (queued, bool) {
	for len(*q) > 0 && (*q)[0].seg < t.rxZ {
		*q = (*q)[1:]
	}
	if len(*q) == 0 {
		return queued{}, false
	}
	h := (*q)[0]
	*q = (*q)[1:]
	return h, true
}

func (t *tap) open(from string) *result {
	if t.cur == nil {
		t.cur = &result{from: from}
	}
	return t.cur
}

func (t *tap) finish() { t.done = append(t.done, *t.cur); t.cur = nil }

func fieldsOf(fds []pgproto3.FieldDescription) []field {
	out := make([]field, len(fds))
	for i, f := range fds {
		out[i] = field{name: string(f.Name), oid: f.DataTypeOID, format: f.Format}
	}
	return out
}

func withFormats(fs []field, formats []int16) []field {
	out := append([]field(nil), fs...)
	for i := range out {
		switch {
		case len(formats) == 0:
			out[i].format = pgtype.TextFormatCode
		case len(formats) == 1:
			out[i].format = formats[0]
		case i < len(formats):
			out[i].format = formats[i]
		}
	}
	return out
}

// received reads what the server answered.
func (t *tap) received(p []byte) {
	t.mu.Lock()
	defer t.mu.Unlock()
	t.rx = append(t.rx, p...)
	for {
		typ, body, ok := frame(&t.rx)
		if !ok {
			return
		}
		switch typ {
		case 't': // ParameterDescription answers a Describe of a statement
			t.stmtDesc = true
		case 'T':
			var m pgproto3.RowDescription
			if m.Decode(body) != nil {
				continue
			}
			if t.stmtDesc {
				if q, ok := t.pop(&t.describes); ok {
					t.stmts[q.name] = fieldsOf(m.Fields)
				}
				t.stmtDesc = false
			} else {
				r := t.open("")
				r.fields, r.from = fieldsOf(m.Fields), "its own RowDescription"
			}
		case 'n':
			if t.stmtDesc {
				if q, ok := t.pop(&t.describes); ok {
					t.stmts[q.name] = nil
				}
				t.stmtDesc = false
			}
		case '2': // BindComplete: the portal about to run has its statement's columns, in Bind's formats
			if q, ok := t.pop(&t.binds); ok {
				t.cur = &result{fields: withFormats(t.stmts[q.name], q.formats), from: "the statement described earlier on this connection"}
			}
		case 'D':
			var m pgproto3.DataRow
			if m.Decode(body) == nil {
				r := t.open("no description")
				r.rows = append(r.rows, m.Values)
			}
		case 'C':
			var m pgproto3.CommandComplete
			if m.Decode(body) == nil {
				t.open("no description").tag = string(m.CommandTag)
				t.finish()
			}
		case 'E':
			var m pgproto3.ErrorResponse
			if m.Decode(body) == nil {
				t.open("no description").err = m.Code + " " + m.Message
				t.finish()
			}
		case 'Z':
			t.rxZ++
			t.cur, t.stmtDesc = nil, false
			for _, q := range []*[]queued{&t.describes, &t.binds} { // what the server skipped after an error
				for len(*q) > 0 && (*q)[0].seg < t.rxZ {
					*q = (*q)[1:]
				}
			}
		}
	}
}

func (t *tap) take() []result {
	t.mu.Lock()
	defer t.mu.Unlock()
	r := t.done
	t.done = nil
	return r
}

func (t *tap) takeOne() []result {
	t.mu.Lock()
	defer t.mu.Unlock()
	if len(t.done) == 0 {
		return nil
	}
	r := t.done[:1]
	t.done = t.done[1:]
	return r
}

func (t *tap) takeCopy() []byte {
	t.mu.Lock()
	defer t.mu.Unlock()
	d := t.copyData
	t.copyData = nil
	return d
}

func (t *tap) fieldsForSQL(sql string) []field {
	t.mu.Lock()
	defer t.mu.Unlock()
	for name, s := range t.stmtSQL {
		if s == sql {
			return t.stmts[name]
		}
	}
	return nil
}

// tapConn is the AfterNetConnect wrapper: every byte the connection carries, after TLS.
type tapConn struct {
	net.Conn
	t *tap
}

func (c *tapConn) Read(p []byte) (int, error) {
	n, err := c.Conn.Read(p)
	if n > 0 {
		c.t.received(p[:n])
	}
	return n, err
}

func (c *tapConn) Write(p []byte) (int, error) {
	c.t.sent(p) // before the write: the answer can arrive on another goroutine before Write returns
	return c.Conn.Write(p)
}

// The BuildFrontend route (pgx before 5.8): the frontend's reader and writer, keyed by the frontend.
type readerFunc func([]byte) (int, error)

func (f readerFunc) Read(p []byte) (int, error) { return f(p) }

type writerFunc func([]byte) (int, error)

func (f writerFunc) Write(p []byte) (int, error) { return f(p) }

var (
	frontMu   sync.Mutex
	frontTaps = map[*pgproto3.Frontend]*tap{}
)

func tapOf(c *pgx.Conn) *tap {
	if tc, ok := c.PgConn().Conn().(*tapConn); ok {
		return tc.t
	}
	frontMu.Lock()
	defer frontMu.Unlock()
	if t := frontTaps[c.PgConn().Frontend()]; t != nil {
		return t
	}
	return newTap() // no tap installed: an empty one, so the tracer-only view still prints
}

// ---------------------------------------------------------------- printing

var (
	pids  = map[uint32]int{}
	stats struct{ traced, tracedRows, untraced, untracedRows, acquires, releases int }
)

func connName(c *pgx.Conn) string {
	pid := c.PgConn().PID()
	if _, ok := pids[pid]; !ok {
		pids[pid] = len(pids) + 1
	}
	return fmt.Sprintf("conn#%d", pids[pid])
}

func say(format string, a ...any) { fmt.Printf("  "+format+"\n", a...) }

func oneLine(s string) string {
	s = strings.Join(strings.Fields(s), " ")
	if len(s) > 72 {
		s = s[:69] + "..."
	}
	return s
}

func fmtArgs(args []any) string {
	parts := make([]string, len(args))
	for i, a := range args {
		switch v := a.(type) {
		case pgx.NamedArgs:
			parts[i] = fmt.Sprintf("NamedArgs%v", map[string]any(v))
		case pgx.QueryExecMode:
			parts[i] = "QueryExecMode(" + v.String() + ")"
		default:
			parts[i] = fmt.Sprintf("%v", a)
		}
	}
	return "[" + strings.Join(parts, " ") + "]"
}

func errText(err error) string {
	var pe *pgconn.PgError
	switch {
	case err == nil:
		return "nil"
	case errors.As(err, &pe):
		return "PgError " + pe.Code + " " + pe.Message
	default:
		return err.Error()
	}
}

func value(tm *pgtype.Map, f field, v []byte) string {
	if v == nil {
		return "NULL"
	}
	if typ, ok := tm.TypeForOID(f.oid); ok {
		if val, err := typ.Codec.DecodeValue(tm, f.oid, f.format, v); err == nil {
			if dv, ok := val.(driver.Valuer); ok {
				if x, err := dv.Value(); err == nil {
					return fmt.Sprint(x)
				}
			}
			return fmt.Sprint(val)
		}
	}
	if f.format == pgtype.TextFormatCode {
		return string(v)
	}
	return fmt.Sprintf("0x%x", v)
}

func typeName(tm *pgtype.Map, f field) string {
	n := fmt.Sprint(f.oid)
	if typ, ok := tm.TypeForOID(f.oid); ok {
		n = typ.Name
	}
	return n + map[int16]string{0: "/text", 1: "/binary"}[f.format]
}

func showRows(tm *pgtype.Map, fields []field, rows [][][]byte) string {
	if len(fields) == 0 && len(rows) == 0 {
		return "no rows"
	}
	cols := make([]string, len(fields))
	for i, f := range fields {
		cols[i] = f.name + " " + typeName(tm, f)
	}
	out := make([]string, len(rows))
	for i, r := range rows {
		vals := make([]string, len(r))
		for j, v := range r {
			f := field{format: pgtype.TextFormatCode}
			if j < len(fields) {
				f = fields[j]
			}
			vals[j] = value(tm, f, v)
		}
		out[i] = "(" + strings.Join(vals, ", ") + ")"
	}
	return fmt.Sprintf("columns [%s] rows %s", strings.Join(cols, ", "), strings.Join(out, " "))
}

// outcome is what the wire said about one statement.
func outcome(c *pgx.Conn, r result) string {
	switch {
	case r.err != "":
		return "error " + r.err
	case len(r.fields) == 0 && len(r.rows) == 0:
		return r.tag
	default:
		return fmt.Sprintf("%s %s; columns from %s", r.tag, showRows(c.TypeMap(), r.fields, r.rows), r.from)
	}
}

func wire(c *pgx.Conn, rs []result) {
	for _, r := range rs {
		stats.traced++
		stats.tracedRows += len(r.rows)
		say("        wire:   %s", outcome(c, r))
	}
}

// untraced prints what the connection carried outside every tracer callback, with the test the pool's
// acquire tracer says held the connection at the time.
func untraced(c *pgx.Conn, when string) {
	for _, r := range tapOf(c).take() {
		stats.untraced++
		stats.untracedRows += len(r.rows)
		holder := tapOf(c).holder
		if holder == "" {
			holder = "(none)"
		}
		say("%-6s  wire, in no tracer callback (%s; the pool had lent %s to %s): %s", holder, when, connName(c), holder, outcome(c, r))
	}
}

// ---------------------------------------------------------------- the tracer: every interface pgx has

type copyKey struct{}

type tracer struct{}

func (tracer) TraceQueryStart(ctx context.Context, c *pgx.Conn, d pgx.TraceQueryStartData) context.Context {
	untraced(c, "before a query")
	say("%-6s  query    %s sql=%q args=%s", testOf(ctx), connName(c), oneLine(d.SQL), fmtArgs(d.Args))
	return ctx
}

func (tracer) TraceQueryEnd(ctx context.Context, c *pgx.Conn, d pgx.TraceQueryEndData) {
	say("%-6s  ...end   tag=%q err=%s", testOf(ctx), d.CommandTag.String(), errText(d.Err))
	wire(c, tapOf(c).take())
}

func (tracer) TraceBatchStart(ctx context.Context, c *pgx.Conn, d pgx.TraceBatchStartData) context.Context {
	untraced(c, "before a batch")
	say("%-6s  batch    %s %d queued", testOf(ctx), connName(c), d.Batch.Len())
	return ctx
}

func (tracer) TraceBatchQuery(ctx context.Context, c *pgx.Conn, d pgx.TraceBatchQueryData) {
	say("%-6s  ...query sql=%q args=%s tag=%q err=%s", testOf(ctx), oneLine(d.SQL), fmtArgs(d.Args), d.CommandTag.String(), errText(d.Err))
	wire(c, tapOf(c).takeOne())
}

func (tracer) TraceBatchEnd(ctx context.Context, c *pgx.Conn, d pgx.TraceBatchEndData) {
	say("%-6s  ...end   err=%s", testOf(ctx), errText(d.Err))
}

func (tracer) TraceCopyFromStart(ctx context.Context, c *pgx.Conn, d pgx.TraceCopyFromStartData) context.Context {
	untraced(c, "before a copy")
	say("%-6s  copy     %s table=%s columns=%v", testOf(ctx), connName(c), d.TableName.Sanitize(), d.ColumnNames)
	return context.WithValue(ctx, copyKey{}, d)
}

func (tracer) TraceCopyFromEnd(ctx context.Context, c *pgx.Conn, d pgx.TraceCopyFromEndData) {
	say("%-6s  ...end   tag=%q err=%s", testOf(ctx), d.CommandTag.String(), errText(d.Err))
	t := tapOf(c)
	start, _ := ctx.Value(copyKey{}).(pgx.TraceCopyFromStartData)
	cols := make([]string, len(start.ColumnNames))
	for i, n := range start.ColumnNames {
		cols[i] = pgx.Identifier{n}.Sanitize()
	}
	// pgx learns the column types by describing this statement first (copy_from.go); so does the tap.
	fields := withFormats(t.fieldsForSQL(fmt.Sprintf("select %s from %s", strings.Join(cols, ", "), start.TableName.Sanitize())), []int16{pgtype.BinaryFormatCode})
	rows := binaryCopyRows(t.takeCopy())
	stats.tracedRows += len(rows)
	say("        wire:   the COPY data: %s", showRows(c.TypeMap(), fields, rows))
	wire(c, t.take())
}

// binaryCopyRows splits PostgreSQL's binary COPY format into rows of fields.
func binaryCopyRows(b []byte) [][][]byte {
	const sig = "PGCOPY\n\377\r\n\000"
	if len(b) < len(sig)+8 || string(b[:len(sig)]) != sig {
		return nil
	}
	b = b[len(sig)+4:]
	ext := int(binary.BigEndian.Uint32(b))
	b = b[4+ext:]
	var rows [][][]byte
	for len(b) >= 2 {
		n := int16(binary.BigEndian.Uint16(b))
		b = b[2:]
		if n < 0 {
			break
		}
		row := make([][]byte, n)
		for i := range row {
			l := int32(binary.BigEndian.Uint32(b))
			b = b[4:]
			if l >= 0 {
				row[i], b = b[:l], b[l:]
			}
		}
		rows = append(rows, row)
	}
	return rows
}

func (tracer) TracePrepareStart(ctx context.Context, c *pgx.Conn, d pgx.TracePrepareStartData) context.Context {
	untraced(c, "before a prepare")
	say("%-6s  prepare  %s name=%q sql=%q", testOf(ctx), connName(c), d.Name, oneLine(d.SQL))
	return ctx
}

func (tracer) TracePrepareEnd(ctx context.Context, c *pgx.Conn, d pgx.TracePrepareEndData) {
	say("%-6s  ...end   alreadyPrepared=%v err=%s", testOf(ctx), d.AlreadyPrepared, errText(d.Err))
	wire(c, tapOf(c).take()) // a statement that fails to parse is answered here
}

func (tracer) TraceConnectStart(ctx context.Context, d pgx.TraceConnectStartData) context.Context {
	say("%-6s  connect  %s:%d database=%s", testOf(ctx), d.ConnConfig.Host, d.ConnConfig.Port, d.ConnConfig.Database)
	return ctx
}

func (tracer) TraceConnectEnd(ctx context.Context, d pgx.TraceConnectEndData) {
	if d.Conn == nil {
		say("%-6s  ...end   err=%s", testOf(ctx), errText(d.Err))
		return
	}
	nc := d.Conn.PgConn().Conn()
	if tc, ok := nc.(*tapConn); ok {
		nc = tc.Conn
	}
	_, isTLS := nc.(*tls.Conn)
	tapOf(d.Conn).take() // startup produces no statements
	say("%-6s  ...end   %s server_version=%s tls=%v err=%s", testOf(ctx), connName(d.Conn), d.Conn.PgConn().ParameterStatus("server_version"), isTLS, errText(d.Err))
}

func (tracer) TraceAcquireStart(ctx context.Context, _ *pgxpool.Pool, _ pgxpool.TraceAcquireStartData) context.Context {
	return ctx
}

func (tracer) TraceAcquireEnd(ctx context.Context, _ *pgxpool.Pool, d pgxpool.TraceAcquireEndData) {
	stats.acquires++
	if d.Conn != nil {
		tapOf(d.Conn).holder = testOf(ctx)
	}
}

func (tracer) TraceRelease(_ *pgxpool.Pool, d pgxpool.TraceReleaseData) {
	stats.releases++
	untraced(d.Conn, "found at release")
	tapOf(d.Conn).holder = ""
}

// ---------------------------------------------------------------- the run

func must(err error) {
	if err != nil {
		fmt.Println("  FAILED:", errText(err))
		os.Exit(1)
	}
}

func collect(rows pgx.Rows, err error) {
	must(err)
	_, err = pgx.CollectRows(rows, pgx.RowToMap)
	must(err)
}

func workload(ctx0 context.Context, pool *pgxpool.Pool) {
	t1, t2 := withTest(ctx0, "T1"), withTest(ctx0, "T2")
	fmt.Println("  -- Exec, and QueryRow with RETURNING")
	_, err := pool.Exec(t1, `drop table if exists h15_orders`)
	must(err)
	_, err = pool.Exec(t1, `create table h15_orders (id bigint generated always as identity primary key, sku text unique not null, qty int not null, price numeric(10,2), meta jsonb)`)
	must(err)
	var id int64
	must(pool.QueryRow(t1, `insert into h15_orders (sku, qty, price, meta) values ($1, $2, $3, $4) returning id`, "ALICE-1", 2, "12.50", map[string]any{"gift": true}).Scan(&id))
	_, err = pool.Exec(t1, `insert into h15_orders (sku, qty, price) values ($1, $2, $3)`, "ALICE-2", 5, "3.10")
	must(err)
	fmt.Println("  -- Query twice: the second takes pgx's statement cache, so no RowDescription crosses the wire")
	for i := 0; i < 2; i++ {
		collect(pool.Query(t1, `select id, sku, qty, price, meta from h15_orders where qty > $1 order by id`, 1))
	}
	fmt.Println("  -- NamedArgs, and the simple protocol")
	collect(pool.Query(t1, `select sku, qty from h15_orders where sku = @sku`, pgx.NamedArgs{"sku": "ALICE-1"}))
	collect(pool.Query(t1, `select sku, price from h15_orders where qty >= $1 order by sku`, pgx.QueryExecModeSimpleProtocol, 2))
	fmt.Println("  -- SendBatch, one query of it failing")
	b := &pgx.Batch{}
	b.Queue(`insert into h15_orders (sku, qty) values ($1, $2)`, "BOB-22", 1)
	b.Queue(`select sku, qty from h15_orders where sku like $1 order by sku`, "ALICE-%")
	b.Queue(`insert into h15_orders (sku, qty) values ($1, $2)`, "ALICE-1", 9)
	pool.SendBatch(t2, b).Close()
	fmt.Println("  -- CopyFrom")
	_, err = pool.CopyFrom(t1, pgx.Identifier{"h15_orders"}, []string{"sku", "qty"}, pgx.CopyFromRows([][]any{{"CAROL-3", 4}, {"CAROL-4", 6}}))
	must(err)
	fmt.Println("  -- Prepare on an acquired connection; then pgconn directly and Ping, which no tracer sees")
	conn, err := pool.Acquire(t2)
	must(err)
	_, err = conn.Conn().Prepare(t2, "by_sku", `select id, qty from h15_orders where sku = $1`)
	must(err)
	collect(conn.Conn().Query(t2, "by_sku", "CAROL-3"))
	_, err = conn.Conn().PgConn().Exec(t2, `select 42 as answer, 'below pgx' as note`).ReadAll()
	must(err)
	must(conn.Conn().Ping(t2))
	conn.Release()
	fmt.Println("  -- a transaction")
	must(pgx.BeginFunc(t1, pool, func(tx pgx.Tx) error {
		_, err := tx.Exec(t1, `delete from h15_orders where sku = $1`, "BOB-22")
		return err
	}))
	fmt.Println("  -- a call whose context carries no test")
	collect(pool.Query(ctx0, `select count(*) as n from h15_orders`))
	fmt.Println("  -- an error")
	_, err = pool.Exec(t1, `insert into h15_orders (sku, qty) values ($1, $2)`, "ALICE-1", 1)
	say("        returned to the caller: %s", errText(err))
}

func main() {
	url := os.Getenv("PGURL")
	if url == "" {
		fmt.Println("set PGURL, e.g. postgres://user:password@127.0.0.1:5432/db")
		os.Exit(2)
	}
	if bi, ok := debug.ReadBuildInfo(); ok {
		for _, m := range bi.Deps {
			if m.Path == "github.com/jackc/pgx/v5" {
				fmt.Printf("H15 %s, %s %s\n", bi.GoVersion, m.Path, m.Version)
			}
		}
	}
	ctx0 := context.Background()
	sep := "?"
	if strings.Contains(url, "?") {
		sep = "&"
	}

	for _, ssl := range []string{"disable", "require"} {
		fmt.Printf("=== H15 pgxpool, tracer + AfterNetConnect tap, sslmode=%s\n", ssl)
		pids = map[uint32]int{}
		stats.traced, stats.tracedRows, stats.untraced, stats.untracedRows, stats.acquires, stats.releases = 0, 0, 0, 0, 0, 0
		cfg, err := pgxpool.ParseConfig(url + sep + "sslmode=" + ssl)
		must(err)
		cfg.MaxConns = 1 // one connection, so the statement cache and the connection names are the same on every run
		cfg.ConnConfig.Tracer = tracer{}
		cfg.ConnConfig.AfterNetConnect = func(_ context.Context, _ *pgconn.Config, c net.Conn) (net.Conn, error) {
			return &tapConn{Conn: c, t: newTap()}, nil
		}
		pool, err := pgxpool.NewWithConfig(ctx0, cfg)
		must(err)
		workload(ctx0, pool)
		pool.Close()
		fmt.Printf("  summary: rows decoded from the wire inside tracer callbacks %d (in %d statements); outside every callback %d statement(s) with %d row(s); pool acquires %d (each with the caller's context), releases %d (no context)\n",
			stats.tracedRows, stats.traced, stats.untraced, stats.untracedRows, stats.acquires, stats.releases)
	}

	fmt.Println("=== H15 the BuildFrontend route (pgx before 5.8), sslmode=require")
	pids = map[uint32]int{}
	cfg, err := pgxpool.ParseConfig(url + sep + "sslmode=require")
	must(err)
	cfg.MaxConns = 1
	cfg.ConnConfig.Tracer = tracer{}
	cfg.ConnConfig.BuildFrontend = func(r io.Reader, w io.Writer) *pgproto3.Frontend {
		t := newTap()
		f := pgproto3.NewFrontend(
			readerFunc(func(p []byte) (int, error) {
				n, err := r.Read(p)
				if n > 0 {
					t.received(p[:n])
				}
				return n, err
			}),
			writerFunc(func(p []byte) (int, error) { t.sent(p); return w.Write(p) }))
		frontMu.Lock()
		frontTaps[f] = t
		frontMu.Unlock()
		return f
	}
	pool, err := pgxpool.NewWithConfig(ctx0, cfg)
	must(err)
	for i := 0; i < 2; i++ {
		collect(pool.Query(withTest(ctx0, "T3"), `select sku, qty from h15_orders where qty > $1 order by sku`, 3))
	}
	pool.Close()

	fmt.Println("=== H15 for contrast: a DialFunc wrapper sits below TLS")
	for _, ssl := range []string{"disable", "require"} {
		var mu sync.Mutex
		var first []byte
		cc, err := pgx.ParseConfig(url + sep + "sslmode=" + ssl)
		must(err)
		d := &net.Dialer{}
		cc.DialFunc = func(ctx context.Context, network, addr string) (net.Conn, error) {
			c, err := d.DialContext(ctx, network, addr)
			return writeSpy{c, func(p []byte) {
				mu.Lock()
				defer mu.Unlock()
				if len(first) < 24 {
					first = append(first, p[:min(len(p), 24-len(first))]...)
				}
			}}, err
		}
		c, err := pgx.ConnectConfig(ctx0, cc)
		must(err)
		c.Close(ctx0)
		verdict := "a StartupMessage: decodable"
		if len(first) >= 9 && binary.BigEndian.Uint32(first[4:8]) == 80877103 {
			verdict = fmt.Sprintf("an SSLRequest, then byte 0x%02x (a TLS handshake record): ciphertext from here on", first[8])
		}
		fmt.Printf("  sslmode=%-7s first bytes the DialFunc conn wrote: % x ... %s\n", ssl, first[:min(len(first), 12)], verdict)
	}

	c, err := pgx.Connect(ctx0, url+sep+"sslmode=disable")
	must(err)
	_, err = c.Exec(ctx0, `drop table h15_orders`)
	must(err)
	c.Close(ctx0)
	fmt.Println("=== H15 done: h15_orders dropped")
}

type writeSpy struct {
	net.Conn
	spy func([]byte)
}

func (w writeSpy) Write(p []byte) (int, error) { w.spy(p); return w.Conn.Write(p) }
