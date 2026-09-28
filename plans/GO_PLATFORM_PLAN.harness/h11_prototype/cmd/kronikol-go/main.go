// kronikol-go is the prototype's run wrapper: `kronikol-go test <go test args>`.
//
// Go leaves three things no in-process adapter can do (H7): the report must wait for every package's
// process, a failure's text exists only in go test's own stream (testing.T returns nothing it
// logged), and a timeout ends the binary without running t.Cleanup. So the wrapper runs `go test
// -json`, and afterwards joins go test's verdicts onto the scenarios the processes wrote, writes the
// one tests file today's ingest takes, and renders with `kronikol ingest` when KRONIKOL_TOOL names it.
package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"sort"
	"strings"
	"time"
)

type goEvent struct {
	Action, Package, Test, Output, Key, Value string
	Elapsed                                   float64
}

type result struct {
	status string // pass, fail, skip, or "" when go test gave no verdict (a timeout)
	output []string
}

func main() {
	if len(os.Args) < 2 || os.Args[1] != "test" {
		fmt.Fprintln(os.Stderr, "usage: kronikol-go test [go test flags] [packages]")
		os.Exit(2)
	}
	run := os.Getenv("KRONIKOL_RUN_DIR")
	if run == "" {
		run = filepath.Join(".kronikol", "runs", time.Now().UTC().Format("20060102T150405Z"))
	}
	run, _ = filepath.Abs(run)
	os.MkdirAll(run, 0o755)

	cmd := exec.Command("go", append([]string{"test", "-json"}, os.Args[2:]...)...)
	// A fresh KRONIKOL_RUN_DIR per run is also the cache key change that re-runs every package whose
	// tests read it, and only those (H7 S3).
	cmd.Env = append(os.Environ(), "KRONIKOL_RUN_DIR="+run)
	cmd.Stderr = os.Stderr
	out, _ := cmd.StdoutPipe()
	if err := cmd.Start(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	results := map[[2]string]*result{}
	var cached, buildFailed []string
	sc := bufio.NewScanner(out)
	sc.Buffer(make([]byte, 1<<20), 1<<24)
	for sc.Scan() {
		var e goEvent
		if json.Unmarshal(sc.Bytes(), &e) != nil {
			continue
		}
		key := [2]string{e.Package, e.Test}
		switch {
		case e.Action == "output" && e.Test == "" && strings.Contains(e.Output, "(cached)"):
			cached = append(cached, e.Package)
		case e.Action == "output" && e.Test == "" && strings.Contains(e.Output, "[build failed]"):
			buildFailed = append(buildFailed, e.Package)
		case e.Test == "":
		case e.Action == "run":
			results[key] = &result{}
		case e.Action == "output" && results[key] != nil:
			results[key].output = append(results[key].output, e.Output)
		case e.Action == "pass" || e.Action == "fail" || e.Action == "skip":
			if results[key] == nil {
				results[key] = &result{}
			}
			results[key].status = e.Action
		}
		if e.Test != "" && (e.Action == "pass" || e.Action == "fail" || e.Action == "skip") {
			fmt.Printf("%-4s %s %s\n", strings.ToUpper(e.Action), e.Package, e.Test)
		}
	}
	err := cmd.Wait()
	code := 0
	if x, ok := err.(*exec.ExitError); ok {
		code = x.ExitCode()
	}

	scenarios, synthesized, uninstrumented := merge(run, results)
	fmt.Printf("\nkronikol-go: %d scenarios in %s; %d end records written from go test's stream (a timeout never runs t.Cleanup)\n",
		scenarios, filepath.Join(run, "tests.ndjson"), synthesized)
	if uninstrumented > 0 {
		fmt.Printf("kronikol-go: %d tests ran without kgo.Start and are not in the report\n", uninstrumented)
	}
	for _, p := range cached {
		fmt.Printf("kronikol-go: WARNING %s was cached: its tests did not run, so nothing was captured for them. Run with -count=1.\n", p)
	}
	for _, p := range buildFailed {
		fmt.Printf("kronikol-go: %s did not build: no scenarios\n", p)
	}

	if tool := os.Getenv("KRONIKOL_TOOL"); tool != "" {
		args := []string{"ingest", filepath.Join(run, "interactions"), "--tests", filepath.Join(run, "tests.ndjson"),
			"-o", filepath.Join(run, "Reports"), "-t", "go test", "--attribute-by-window", "--run-window"}
		c := exec.Command(tool, args...)
		if strings.HasSuffix(tool, ".dll") {
			c = exec.Command("dotnet", append([]string{tool}, args...)...)
		}
		c.Stdout, c.Stderr = os.Stdout, os.Stderr
		if err := c.Run(); err != nil {
			fmt.Fprintln(os.Stderr, "kronikol ingest:", err)
		}
	}
	os.Exit(code)
}

// merge concatenates every process's tests fragment into one file, filling each failed scenario's
// error from go test's output and writing the end record a timed-out or killed test never wrote.
func merge(run string, results map[[2]string]*result) (scenarios, synthesized, uninstrumented int) {
	var records []map[string]any
	starts := map[string]map[string]any{}
	ends := map[string]map[string]any{}
	files, _ := filepath.Glob(filepath.Join(run, "tests", "*.ndjson"))
	for _, f := range files {
		b, _ := os.ReadFile(f)
		for _, line := range strings.Split(strings.TrimSpace(string(b)), "\n") {
			var r map[string]any
			if json.Unmarshal([]byte(line), &r) != nil {
				continue
			}
			records = append(records, r)
			id, _ := r["testId"].(string)
			switch r["event"] {
			case "start":
				starts[id] = r
			case "end":
				ends[id] = r
			}
		}
	}
	seen := map[[2]string]bool{}
	for id, s := range starts {
		key := [2]string{fmt.Sprint(s["feature"]), fmt.Sprint(s["testName"])}
		seen[key] = true
		res := results[key]
		end := ends[id]
		if end == nil {
			end = map[string]any{"event": "end", "testId": id, "status": "interrupted"}
			if res != nil && strings.Contains(strings.Join(res.output, ""), "test timed out") {
				end["status"] = "timedOut"
			}
			records = append(records, end)
			synthesized++
		}
		if res != nil && res.status == "fail" || end["status"] == "timedOut" || end["status"] == "interrupted" {
			if res != nil {
				end["error"] = failureText(res.output)
			}
			if end["status"] == "passed" {
				end["status"] = "failed"
			}
		}
	}
	for key := range results {
		if !seen[key] && !strings.Contains(key[1], "/") {
			uninstrumented++
		}
	}
	sort.SliceStable(records, func(i, j int) bool { return fmt.Sprint(records[i]["timestamp"]) < fmt.Sprint(records[j]["timestamp"]) })
	f, _ := os.Create(filepath.Join(run, "tests.ndjson"))
	defer f.Close()
	for _, r := range records {
		b, _ := json.Marshal(r)
		f.Write(append(b, '\n'))
	}
	return len(starts), synthesized, uninstrumented
}

func failureText(output []string) string {
	var keep []string
	for _, l := range output {
		t := strings.TrimSpace(l)
		if t == "" || strings.HasPrefix(t, "=== ") || strings.HasPrefix(t, "--- ") {
			continue
		}
		keep = append(keep, t)
	}
	return strings.Join(keep, "\n")
}
