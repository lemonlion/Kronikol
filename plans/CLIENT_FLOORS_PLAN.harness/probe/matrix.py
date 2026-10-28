# Reads results/emu<tag>-client<version>-plain.txt and prints one markdown table: a row per scenario, a column per
# emulator and client, each cell what reading the TIMESTAMP did.
import glob, os, re, sys
results = sys.argv[1]
def cell(outcome):
    if outcome == "ok": return "ok"
    m = re.search(r"FormatException: The input string '([^']+)'", outcome)
    if m:
        return ("top-level ok, nested " if outcome.startswith("ts=") else "") + f"FormatException `{m.group(1)}`"
    if "GoogleApiException" in outcome:
        if "BadRequest" in outcome: return "API 400"
        if "InternalServerError" in outcome: return "API 500"
        return "job failed"
    if re.search(r"ts=\d{4}-\d\d-\d\d \d", outcome): return "string, not a time"
    if outcome.startswith("ts=") or outcome.startswith("rec="): return "ok"
    return outcome[:40]
def key(path):
    m = re.search(r"emu([\d.]+)-client([\d.]+)-plain", path)
    tag, client = m.group(1), m.group(2)
    return ([int(x) for x in tag.split(".")], [int(x) for x in client.split(".")])
files = sorted(glob.glob(os.path.join(results, "emu*-client*-plain.txt")), key=key)
columns, table, titles = [], {}, {}
for f in files:
    tag, client = re.search(r"emu([\d.]+)-client([\d.]+)-plain", f).groups()
    col = f"{tag} / {client}"
    columns.append(col)
    title = None
    for line in open(f, encoding="utf-8"):
        if line.startswith("## Q"):
            title = line[3:].strip(); q = title.split()[0]; titles[q] = title
        elif line.startswith("  = ") and title:
            table[(q, col)] = cell(line[4:].strip()); title = None
print("| Scenario | " + " | ".join(columns) + " |")
print("|---|" + "---|" * len(columns))
for q in sorted(titles, key=lambda s: int(s[1:])):
    print(f"| {titles[q]} | " + " | ".join(table.get((q, c), "n/a") for c in columns) + " |")
