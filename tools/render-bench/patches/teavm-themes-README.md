# TeaVM themes patch (SHIPPED 2026-08-31: issue plantuml#2847, PR plantuml#2848)

Makes `!theme <name>` work in the PlantUML browser/JS engine. Live as
<https://github.com/plantuml/plantuml/pull/2848> (fixes issue #2847), fork branch
`lemonlion/plantuml:teavm-themes`, rebased onto master `8c574c5`. PR images live on
orphan branch `assets-teavm-themes` (SHA-pinned raw URLs). On review feedback, push
to the fork branch; the PR updates automatically.

Five commits, deliberately separable so the maintainer can take fewer:

1. `🍵 support !theme in the browser (TeaVM) build` - the fix (8 files)
2. `✅ add browser-test: functional checks for !theme` - `browser-test/check-themes.js`,
   headless Chromium; 8 of 13 fail on a pre-patch engine, all pass after
3. `👷 run browser-test on push and pull request` - CI wiring, droppable
4. `✅ RED: missing themes.js must degrade to unthemed plus a console warning, not an error image`
5. `🍵 GREEN: a missing themes.js degrades to unthemed with a console warning instead of an error image`
   (also guards the latent ReadLineWithYamlHeader NPE on a zero-line source, with a JVM unit test)

Files here:

- `teavm-themes-5commits.patch` all five, `git am` ready
- `teavm-themes-full.diff`      squashed, `git apply`s cleanly to master (verified)
- `teavm-themes-src.diff`       same minus the generated 326 KB `teavm/themes.js`

`themes.js` is deterministic output of the new `ThemesJsGenerator`, so
`teavm-themes-src.diff` alone suffices if you then run, from the repo root:

    java -cp <classes> net.sourceforge.plantuml.theme.ThemesJsGenerator

Build: `gradlew.bat :plantuml-mit:npmPackage -Pci --no-daemon` (JDK 25, ~60 s).
Check: `node browser-test/check-themes.js target=plantuml-mit/build/npm-plantuml`
(set `BENCH_PW` to a playwright install to skip `npm ci`).

PR body draft: `../themes-pr-draft.md`. Screenshots: `../theme-shots/`.
Ad-hoc session scripts kept alongside: `../verify-themes.js`, `../all-themes.js`,
`../corpus-hash.js` (superseded by `browser-test/check-themes.js`, which is the in-repo one).

## Review pass 2026-08-31 (second session)

Fixed before anything ships: getTheme JSBody and the themes.js wrapper resolve the
global via globalThis first (the old 'self : this' threw TypeError in strict/ESM
contexts without self, e.g. plain Node); proven against the compiled engine in Node.
Generator now writes explicit 
 (deterministic bytes on every platform). ThemesJsTest
additionally pins ThemeList against the classpath (a theme added without re-running
EITHER generator now fails). Check script renamed check-themes.js. npm READMEs document
that themes.js must be served next to the engine. Stripped JAVA8 sources proven to
compile at --release 8.

## Separate finding, not fixed here

The TeaVM SVG driver does not paint the diagram background. Java writes
`style="...background:#RRGGBB;"` plus a full-size rect; the browser build writes
neither, for `!theme` and for plain `skinparam backgroundColor` alike, before and
after this patch. 21 of 44 themes set a background in the Java build, 12 of them
non-white, so dark themes (amiga, blueprint, crt-amber, crt-green, ...) still look
wrong in the browser. Worth its own issue/PR against the SVG driver.

## Fallback pass 2026-08-31 (same session, user decision)

A page that upgrades the engine without deploying themes.js no longer gets error
diagrams (that was a breaking change for published pages, worst on the common
CDN-engine + own-origin-page setup, since TeaVmScriptLoader resolves against the
document): !theme now degrades to the unthemed rendering, byte-identical to today,
plus a console.warn naming themes.js. Unknown theme name in a LOADED themes.js is
still an error (typo protection, the point of #2847). The distinguishing signal is
whether globalThis.PLANTUML_THEMES exists after the load attempt (hasThemes()).
check-themes.js is now 13 checks (renders-unthemed + warns replace the old
error-on-missing check). ReadLineWithYamlHeader NPE'd on a zero-line source (empty
theme); guarded + ReadLineWithYamlHeaderTest (fails by NPE without the guard).
JAVA8 strip of ThemeUtils unchanged vs previous head; release-8 compile OK; corpus
22/22 identical; full gradlew test -Pci green; teavm-svg-background rebased on top
(check-background still 16/16). Heads: teavm-themes 898cf60, teavm-svg-background
99c97dc.
