// H10. Can a Go process host the shared renderer in-process? wazero is the pure-Go runtime (no cgo).
// Positive control: a WASI Preview 1 module doing the renderer's file I/O. Negative control: the
// preamble every WebAssembly component starts with, which is what .NET 10's wasi-wasm publish emits
// (PLATFORM_FOUNDATIONS_PLAN §11: header 0d 00 01 00).
package main

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"time"

	"github.com/tetratelabs/wazero"
	"github.com/tetratelabs/wazero/imports/wasi_snapshot_preview1"
)

func main() {
	ctx := context.Background()
	work, _ := os.MkdirTemp("", "h10")
	os.WriteFile(filepath.Join(work, "interactions.ndjson"), []byte("{}\n{}\n{}\n"), 0o644)

	rt := wazero.NewRuntime(ctx)
	defer rt.Close(ctx)
	wasi_snapshot_preview1.MustInstantiate(ctx, rt)

	guest, err := os.ReadFile(os.Args[1])
	if err != nil {
		panic(err)
	}
	start := time.Now()
	compiled, err := rt.CompileModule(ctx, guest)
	if err != nil {
		panic(err)
	}
	compileTime := time.Since(start)
	cfg := wazero.NewModuleConfig().WithArgs("render", "/work").WithStdout(os.Stdout).WithStderr(os.Stderr).
		WithFSConfig(wazero.NewFSConfig().WithDirMount(work, "/work"))
	start = time.Now()
	_, err = rt.InstantiateModule(ctx, compiled, cfg)
	out, _ := os.ReadFile(filepath.Join(work, "report.txt"))
	fmt.Printf("H10 wazero, WASI Preview 1 guest (%d KB): compile %v, run %v, err=%v, wrote %q\n",
		len(guest)/1024, compileTime.Round(time.Millisecond), time.Since(start).Round(time.Millisecond), err, out)

	component := []byte{0x00, 0x61, 0x73, 0x6d, 0x0d, 0x00, 0x01, 0x00} // "\0asm", version 0x0d, layer 1: an empty component
	_, err = rt.CompileModule(ctx, component)
	fmt.Printf("H10 wazero, the component preamble 00 61 73 6d 0d 00 01 00: err = %v\n", err)
}
