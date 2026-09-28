// The guest: what the shared renderer does at the syscall floor (KRONIKOL4J_PORTABILITY_PLAN §8.1),
// read NDJSON from a preopened directory and write a file beside it. Built with GOOS=wasip1.
package main

import (
	"bufio"
	"fmt"
	"os"
)

func main() {
	f, err := os.Open("/work/interactions.ndjson")
	if err != nil {
		fmt.Fprintln(os.Stderr, "open:", err)
		os.Exit(1)
	}
	lines := 0
	for s := bufio.NewScanner(f); s.Scan(); lines++ {
	}
	f.Close()
	os.WriteFile("/work/report.txt", []byte(fmt.Sprintf("%d lines\n", lines)), 0o644)
	fmt.Println("guest read", lines, "lines, argv", os.Args[1:])
}
