"""Check and fix [[Page#Heading]] wiki links against GitHub's rendering rules.

GitHub renders [[a|b]] as label a, target b, and keeps a #fragment as written apart from
spaces -> hyphens and '+' dropped, while heading ids are slugs (lower case; everything that is
not a word character, a hyphen or a space removed; spaces to hyphens; duplicates numbered).
So a fragment written as the heading text never matches its heading. This script:

  check              report every [[...]] link: ok / relabel / fix / dead / reversed / dead-page
  fix                rewrite the fixable ones in place (label kept; a bare anchor link gets one)
  verify-live X.html Y.md   compare computed heading slugs with the ids in a rendered page
"""
import os
import re
import sys
import glob

WIKI = os.environ.get("KRONIKOL_WIKI") or os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "Kronikol.wiki"))

# html-pipeline's TableOfContentsFilter: /[^\p{Word}\- ]/u removed, then spaces to hyphens.
NOT_WORD = re.compile(r"[^\w\- ]")

# Pages linked as [[Integration: X Extension]] that exist under another name. GitHub keeps the
# colon (as %3A) and so lands on an absent page.
PAGE_ALIASES = {
    "Integration: BigQuery Extension": "Integration-BigQuery-Extension",
    "Integration: BlobStorage Extension": "Integration-BlobStorage-Extension",
    "Integration: Amazon S3 Extension": "Integration-S3-Extension",
    "Integration: ServiceBus Extension": "Integration-ServiceBus-Extension",
    "Integration: PubSub Extension": "Integration-PubSub-Extension",
    "Integration: SQS Extension": "Integration-SQS-Extension",
    "Integration: Amazon DynamoDB Extension": "Integration-DynamoDB-Extension",
    "Integration: Azure Service Bus Extension": "Integration-ServiceBus-Extension",
    "Integration: Amazon SQS Extension": "Integration-SQS-Extension",
}


def rendered_text(md):
    """The text GitHub slugs: code spans verbatim (minus the backticks), markup elsewhere removed."""
    out = []
    for i, part in enumerate(re.split(r"(`[^`]*`)", md.strip())):
        if i % 2 == 1:
            out.append(part[1:-1])
            continue
        part = re.sub(r"!?\[([^\]]*)\]\([^)]*\)", r"\1", part)
        part = re.sub(r"\*\*(.+?)\*\*", r"\1", part)
        part = re.sub(r"\*(.+?)\*", r"\1", part)
        part = re.sub(r"<[^>]+>", "", part)
        out.append(part)
    return "".join(out)


def slugify(text):
    t = rendered_text(text).lower()
    t = NOT_WORD.sub("", t)
    return t.replace(" ", "-")


def strip_fences(lines):
    """Yield (index, line) for lines outside fenced code blocks."""
    fence = None
    for i, line in enumerate(lines):
        m = re.match(r"^\s*(`{3,}|~{3,})", line)
        if m:
            if fence is None:
                fence = m.group(1)[0]
            elif line.strip().startswith(fence * 3):
                fence = None
            continue
        if fence is None:
            yield i, line


def headings(path):
    """Return [(slug, text)] in document order, with GitHub's duplicate numbering."""
    with open(path, encoding="utf-8") as f:
        lines = f.read().split("\n")
    seen = {}
    out = []
    for _, line in strip_fences(lines):
        m = re.match(r"^(#{1,6})\s+(.*?)\s*#*\s*$", line)
        if not m:
            continue
        text = rendered_text(m.group(2))
        base = slugify(m.group(2))
        n = seen.get(base, 0)
        seen[base] = n + 1
        out.append((base if n == 0 else f"{base}-{n}", text))
    return out


def page_files():
    return {os.path.splitext(os.path.basename(p))[0]: p for p in glob.glob(os.path.join(WIKI, "*.md"))}


def resolve_page(name, files, current):
    if name == "":
        return current
    key = PAGE_ALIASES.get(name.strip(), name.strip()).replace(" ", "-").lower()
    for stem, path in files.items():
        if stem.lower() == key:
            return path
    return None


def parse_target(target, files, current):
    if target.startswith("#"):
        return resolve_page("", files, current), target[1:], ""
    if "#" in target:
        page, frag = target.split("#", 1)
        return resolve_page(page, files, current), frag, page
    return resolve_page(target, files, current), None, target


def mask_code_spans(line):
    return re.sub(r"`[^`]*`", lambda m: "x" * len(m.group(0)), line)


LINK = re.compile(r"\[\[(.+?)\]\]")


def match_fragment(frag, slugs):
    """The heading slug a fragment means: exact, slugified, version suffix dropped, unique prefix."""
    if frag in slugs:
        return frag, "exact"
    s = slugify(frag)
    if s in slugs:
        return s, "slug"
    stripped = slugify(re.sub(r"\s*\(v[0-9.]+\+?\)\s*$", "", frag))
    if stripped in slugs:
        return stripped, "slug"
    prefixed = [h for h in slugs if h.startswith(s + "-") or h.startswith(s)]
    if len(prefixed) == 1 and len(s) >= 12:
        return prefixed[0], "prefix"
    return None, None


def analyse(files):
    """Yield one record per [[...]] link."""
    for stem, path in sorted(files.items()):
        with open(path, encoding="utf-8") as f:
            lines = f.read().split("\n")
        for i, line in strip_fences(lines):
            masked = mask_code_spans(line)
            for m in LINK.finditer(masked):
                raw = line[m.start(1):m.end(1)]
                parts = re.split(r"\\?\|", raw, maxsplit=1)
                escaped = "\\|" in raw
                label, target, reversed_ = (None, raw, False) if len(parts) == 1 else (parts[0], parts[1], False)
                if len(parts) == 2:
                    left_page, _, _ = parse_target(parts[0], files, path)
                    right_page, _, _ = parse_target(parts[1], files, path)
                    if left_page is not None and right_page is None:
                        label, target, reversed_ = parts[1], parts[0], True
                page_path, frag, page_as_written = parse_target(target, files, path)
                rec = {
                    "file": stem, "line": i + 1, "raw": raw, "span": (m.start(), m.end()),
                    "label": label, "target": target, "reversed": reversed_, "escaped": escaped,
                    "page_as_written": page_as_written, "new_frag": None, "heading": None,
                }
                if page_path is None:
                    rec["status"] = "dead-page"
                    yield rec
                    continue
                rec["page_file"] = os.path.splitext(os.path.basename(page_path))[0]
                aliased = page_as_written.strip() in PAGE_ALIASES
                if frag is None:
                    rec["status"] = "reversed-page" if reversed_ else ("alias-page" if aliased else "page-ok")
                    yield rec
                    continue
                slugs = {s: t for s, t in headings(page_path)}
                found, how = match_fragment(frag, slugs)
                if found is None:
                    rec["status"] = "dead"
                    words = slugify(frag).split("-")[:2]
                    rec["candidates"] = [s for s in slugs if all(w in s for w in words if w)][:6]
                    yield rec
                    continue
                rec["new_frag"] = found
                rec["heading"] = slugs[found]
                if how == "exact":
                    rec["status"] = "reversed" if reversed_ else ("relabel" if label is None else "ok")
                    if rec["status"] == "ok" and page_as_written == "":
                        rec["status"] = "self"
                else:
                    rec["status"] = "fix"
                yield rec


REWRITTEN = ("fix", "reversed", "relabel", "reversed-page", "alias-page", "self")


def rewrite(rec):
    """The replacement text, keeping the author's label when there is one."""
    page = rec["page_as_written"].strip()
    hyphenated = PAGE_ALIASES.get(page, page).replace(" ", "-")
    if rec["new_frag"] is None:
        target = hyphenated
    else:
        target = f"{hyphenated}#{rec['new_frag']}" if page else f"{rec['file']}#{rec['new_frag']}"
    label = rec["label"]
    if label is None:
        display = page.replace("-", " ") if page and " " not in page else page
        if rec["heading"] is None:
            label = page
        else:
            label = f"{display} › {rec['heading']}" if page else rec["heading"]
    pipe = "\\|" if rec["escaped"] else "|"
    return f"[[{label}{pipe}{target}]]"


def cmd_check(files):
    counts = {}
    for rec in analyse(files):
        counts[rec["status"]] = counts.get(rec["status"], 0) + 1
        if rec["status"] in ("page-ok", "ok"):
            continue
        extra = ""
        if rec["status"] in REWRITTEN:
            extra = " -> " + rewrite(rec)
        if rec["status"] == "dead":
            extra = " candidates=" + ", ".join(rec["candidates"])
        print(f"{rec['status']:13} {rec['file']}:{rec['line']}  [[{rec['raw']}]]{extra}")
    print("counts:", counts)


def cmd_fix(files):
    changed = {}
    for rec in analyse(files):
        if rec["status"] not in REWRITTEN:
            continue
        changed.setdefault(rec["file"], []).append(rec)
    total = 0
    for stem, recs in changed.items():
        path = files[stem]
        with open(path, encoding="utf-8", newline="") as f:
            text = f.read()
        nl = "\r\n" if "\r\n" in text else "\n"
        lines = text.split(nl)
        for rec in sorted(recs, key=lambda r: (r["line"], -r["span"][0])):
            line = lines[rec["line"] - 1]
            s, e = rec["span"]
            assert line[s:e] == "[[" + rec["raw"] + "]]", (stem, rec["line"], line[s:e])
            lines[rec["line"] - 1] = line[:s] + rewrite(rec) + line[e:]
            total += 1
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(nl.join(lines))
    print(f"rewrote {total} links in {len(changed)} pages")


def cmd_verify_live(html_path, md_path):
    with open(html_path, encoding="utf-8", errors="replace") as f:
        html = f.read()
    live = set(re.findall(r'id="user-content-([^"]+)"', html))
    computed = [s for s, _ in headings(md_path)]
    missing = [s for s in computed if s not in live]
    print(f"{os.path.basename(md_path)}: {len(computed)} headings computed, {len(live)} live ids; "
          f"computed-but-not-live={missing}")


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "check"
    files = page_files()
    if mode == "check":
        cmd_check(files)
    elif mode == "fix":
        cmd_fix(files)
    elif mode == "verify-live":
        cmd_verify_live(sys.argv[2], sys.argv[3])
    else:
        raise SystemExit("mode?")
