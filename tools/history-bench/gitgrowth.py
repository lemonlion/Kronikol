"""What a committed ledger costs a repository, measured. CROSS_RUN_HISTORY_PLAN §5.10 / §17.2.

Simulates N CI builds against a 203-scenario suite (the real BreakfastProvider shape, §2.1),
committing after each, for three storage designs. Usage: python gitgrowth.py [builds] [window]
"""
import hashlib, json, os, random, shutil, subprocess, sys, tempfile

BUILDS = int(sys.argv[1]) if len(sys.argv) > 1 else 500
WINDOW = int(sys.argv[2]) if len(sys.argv) > 2 else 50
N = 203                      # scenarios, from the measured suite
random.seed(7)

ids    = [hashlib.sha256(str(i).encode()).hexdigest()[:16] for i in range(N)]
shapes = [hashlib.sha256(("s%d" % i).encode()).hexdigest()[:8] for i in range(N)]
roster = json.dumps({"t": "roster", "hash": "r" + hashlib.sha256("".join(ids).encode()).hexdigest()[:12],
                     "suite": "BreakfastProvider.Tests.Component.BDDfy", "ids": ids},
                    separators=(",", ":"))

def run_line(i):
    """One realistic run: ~4% failures, durations jittered, shapes almost always stable."""
    results = "".join("F" if random.random() < 0.04 else "P" for _ in range(N))
    return json.dumps({"t": "run", "id": "gh:%d:1" % (18273645 + i), "partial": False,
                       "at": "2026-09-%02dT%02d:04:11Z" % (1 + i % 28, i % 24), "branch": "main",
                       "commit": hashlib.sha256(str(i).encode()).hexdigest()[:7],
                       "roster": "r" + hashlib.sha256("".join(ids).encode()).hexdigest()[:12],
                       "results": results,
                       "durations": [int(1200 * (0.8 + random.random() * 0.4)) for _ in range(N)],
                       "shapeSet": [s if random.random() > 0.002 else "ffffffff" for s in shapes]},
                      separators=(",", ":"))

def git(d, *a):
    subprocess.run(["git", "-C", d] + list(a), check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

def size(d):
    return sum(os.path.getsize(os.path.join(r, f))
               for r, _, fs in os.walk(d) for f in fs) / 1024.0 / 1024.0

def simulate(name, write):
    d = tempfile.mkdtemp()
    git(d, "init", "-q", "."); git(d, "config", "user.email", "t@t"); git(d, "config", "user.name", "t")
    git(d, "config", "commit.gpgsign", "false")
    lines = []
    for i in range(BUILDS):
        lines.append(run_line(i))
        write(d, lines[-WINDOW:] if WINDOW else lines)
        git(d, "add", "-A"); git(d, "commit", "-qm", "history: run %d" % i)
    loose = size(os.path.join(d, ".git"))
    git(d, "gc", "-q", "--aggressive", "--prune=now")
    packed = size(os.path.join(d, ".git"))
    working = os.path.getsize(os.path.join(d, "KronikolHistory.jsonl")) / 1024.0
    shutil.rmtree(d, ignore_errors=True)
    print("%-34s working %7.1f KB   .git loose %6.2f MB   packed %6.2f MB   per build %5.1f KB"
          % (name, working, loose, packed, packed * 1024 / BUILDS))

def jsonl(d, window_lines):
    with open(os.path.join(d, "KronikolHistory.jsonl"), "w", newline="\n") as f:
        f.write('{"t":"header","historyFormatVersion":1}\n' + roster + "\n")
        f.write("\n".join(window_lines) + "\n")

def document(d, window_lines):
    """The rewritten-JSON alternative §3.1 rejected: same data, re-serialised whole, pretty-printed."""
    with open(os.path.join(d, "KronikolHistory.jsonl"), "w", newline="\n") as f:
        json.dump({"historyFormatVersion": 1, "roster": json.loads(roster),
                   "runs": [json.loads(l) for l in window_lines]}, f, indent=2)

print("%d builds, %d-run window, %d scenarios\n" % (BUILDS, WINDOW, N))
simulate("append-only JSONL (window %d)" % WINDOW, jsonl)
simulate("rewritten JSON document", document)
