// H10b. The .NET 10 WASI build's real output (dotnet.wasm, the prebuilt Mono runtime component) under
// the two Go hosts: wazero, pure Go, and wasmtime-go, which binds Wasmtime's C API through cgo and
// is the only Go binding with the component model.
package main

import (
	"context"
	"fmt"
	"os"
	"strings"

	"github.com/bytecodealliance/wasmtime-go/v49"
	"github.com/tetratelabs/wazero"
)

func main() {
	wasm, err := os.ReadFile(os.Args[1])
	if err != nil {
		panic(err)
	}
	fmt.Printf("H10b %s: %d bytes, preamble % x\n", os.Args[1][strings.LastIndex(os.Args[1], "/")+1:], len(wasm), wasm[:8])

	rt := wazero.NewRuntime(context.Background())
	_, err = rt.CompileModule(context.Background(), wasm)
	fmt.Printf("  wazero v1.12.0 CompileModule: err = %v\n", err)

	engine := wasmtime.NewEngine()
	comp, err := wasmtime.NewComponent(engine, wasm)
	fmt.Printf("  wasmtime-go v49 NewComponent: err = %v\n", err)
	if err != nil {
		return
	}
	linker := wasmtime.NewComponentLinker(engine)
	store := wasmtime.NewStore(engine)
	_, err = linker.Instantiate(store, comp)
	msg := fmt.Sprint(err)
	if len(msg) > 220 {
		msg = msg[:220] + "..."
	}
	fmt.Printf("  wasmtime-go v49 ComponentLinker.Instantiate with nothing defined: err = %s\n", msg)
	fmt.Println("  the ComponentLinker's methods at v49.0.0: Instantiate, DefineUnknownImportsAsTraps, Close;")
	fmt.Println("  no WASI Preview 2 definer (component_linker_feat_component_model.go:81, \"TODO: WASIp2 / wasi:http integration\")")
}
