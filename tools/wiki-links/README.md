# wiki-links

Checks and fixes every `[[...]]` link in the wiki checkout at `../Kronikol.wiki` against GitHub's
rendering rules, which were measured on the rendered wiki on 2026-09-22:

- `[[a|b]]` is label `a`, target `b`. A link written target-first lands on an absent page.
- A `[[Page#Fragment]]` fragment is kept as written (spaces to hyphens, `+` dropped, case and
  punctuation kept), while heading ids are slugs: lower case, everything that is not a word
  character, a hyphen or a space removed, spaces to hyphens, duplicates numbered `-1`, `-2`.
  So a fragment written as the heading text never resolves.
- A page name keeps its colon (`Integration: X Extension` becomes `Integration:-X-Extension`).

```
python tools/wiki-links/wikilinks.py check                      # report: fix / dead / reversed / dead-page
python tools/wiki-links/wikilinks.py fix                        # rewrite what can be rewritten
python tools/wiki-links/wikilinks.py verify-live page.html Page.md   # compare slugs with a saved rendered page
```

`check` should print `counts: {'ok': N, 'page-ok': M}` and nothing else. A `dead` line names a
heading that no longer exists and needs a hand fix; a `dead-page` line names a page that does not
exist under that spelling (add it to `PAGE_ALIASES` if it exists under another).

Run with `PYTHONUTF8=1` on Windows, or the console code page rejects the labels the script prints.
