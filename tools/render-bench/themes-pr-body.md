Fixes #2847. First of two stacked PRs; they fix two different bugs in the same area:

- **This PR:** `!theme` is silently ignored by the browser engine (#2847). After this PR the themes are applied.
- **#2849** (stacked on this one): even with themes applied, the browser engine never paints the document background, a bug that also exists without themes (`skinparam backgroundColor` is ignored too). Its diff includes this PR's five commits until this PR merges; only its last four commits are new.

## What happens today

In the JS engine, `!theme <name>` is accepted and then silently ignored. No styling
is applied and no error is reported. An unknown theme name behaves the same way as a
real one, so there is no signal that anything went wrong.

```
@startuml
!theme amiga
Alice -> Bob: hello
Bob --> Alice: hi
@enduml
```

Rendered with `@plantuml/core`, the SVG produced for that source is identical to the
SVG produced without the `!theme` line, apart from the embedded `<?plantuml-src ?>`
metadata. The participants keep the default `#E2E2F0` fill instead of the amiga
theme's `#0B58A8` on `#FFFFFF`.

The engine also advertises themes it cannot apply. `%get_all_theme()` returns all 44
bundled names in the browser (this was added for TeaVM in #2725), while
`%get_current_theme()` returns `{}` after a `!theme` line that named one of them.

## Why

Two separate things, both in the browser build only.

1. `TContext.executeTheme` has its whole body inside `if (!TeaVM.isTeaVM())`, so the
   directive is a no-op there. Since `isTeaVM()` is a `@PlatformMarker`, the body is
   also dead-code eliminated, which is why no theme string appears anywhere in the
   compiled `plantuml.js`.

2. `ThemeUtils.loadBundledOrLocalTheme` reads `/themes/puml-theme-<name>.puml` through
   `getResourceAsStream`. The browser build has no classpath, so that lookup could not
   succeed even if the code path were live.

Nothing else is missing. The themes are plain preprocessor sources (`!procedure`,
`!if`, `!foreach`, `!startsub`, `!$VAR`, YAML front matter), none of them use
`!include` or sprites, and all of that machinery already works in the JS build. Pasting
the body of `puml-theme-amiga.puml` directly into a diagram renders correctly today.
The only thing missing is a way to hand the theme text to the preprocessor.

## The change

The same approach the engine already uses for emoji, openiconic and the stdlib
sprite bundles: a generated companion script, loaded on demand through
`TeaVmScriptLoader`, that publishes the data on a global.

- `ThemesJsGenerator` writes `src/main/resources/teavm/themes.js`, mapping each theme
  name to the full text of its `.puml` file on `PLANTUML_THEMES`. This mirrors
  `EmbeddedResourcesGenerator` and `ThemeListGenerator`, and the output is committed
  next to `emoji.js` and `openiconic.js`.
- `TeaVmScriptLoader.getTheme(name)` reads that map.
- `ThemeUtils.loadTheme` gets a TeaVM branch that serves bundled themes from it.
- `TContext.executeTheme` loses its `if (!TeaVM.isTeaVM())` wrapper.
- `themes.js` is added to both `npmPackage` file lists.

The `PLANTUML_THEMES` map is consulted before `themes.js` is fetched, and both the
engine and the generated script resolve the global through `globalThis`, so a host that
registers `globalThis.PLANTUML_THEMES` itself can use themes where the loader cannot
fetch: inside a Web Worker, where `TeaVmScriptLoader` has no `document` to append a
script tag to, or in a non-browser JS runtime. The generator writes its output with
explicit newlines, so regenerating produces the same bytes on every platform.

`ThemesJsTest` fails if `themes.js` or the generated `ThemeList` stop matching the
bundled `.puml` files, so a theme cannot be added or edited without the browser
artifacts being regenerated.

## Size

`themes.js` is 326 KB, 27 KB gzipped, and is only fetched by a diagram that actually
uses `!theme`. For comparison, `emoji.js` in the same package is 1.9 MB and loads the
same way.

The engine itself grows by 4.3 KB (3,906,485 to 3,910,842 bytes), which is the theme
code path no longer being eliminated.

## How to check it yourself

```
gradlew :plantuml-mit:npmPackage -Pci
cd browser-test && npm ci && npx playwright install --with-deps chromium
node check-themes.js target=../plantuml-mit/build/npm-plantuml
```

That is the second commit: `browser-test/`, a functional sibling to `perf-bench`.
perf-bench already renders the browser engine in CI but only measures speed, so nothing
asserted that the engine draws the right thing. These checks do, and they fail the build
when it does not. There are no golden files: the expected rendering for a theme is derived
from that theme's own text read out of the `themes.js` under test, so adding or editing a
theme needs no fixture update.

The strongest of the thirteen checks is that loading a theme by name produces exactly what
pasting that theme's body into the diagram produces. Both sides are rendered by the same
engine, so only the loading path differs.

Pointed at an engine built before this change, 8 of the 13 fail:

```
PASS  control diagram renders
FAIL  !theme amiga changes the output
        identical to the unthemed diagram: the directive was ignored
FAIL  !theme amiga applies the amiga palette
FAIL  !theme amiga == the same theme inlined by hand
FAIL  unknown theme name reports an error
        rendered a normal diagram, so a typo in a theme name would pass unnoticed
FAIL  %get_current_theme() returns the loaded theme metadata
PASS  %get_all_theme() agrees with themes.js (44)
PASS  all 44 bundled themes render
FAIL  all 44 bundled themes change the drawing
        rendered identically to the unthemed diagram, so these were ignored:
        amiga, aws-orange, black-knight, bluegray, blueprint, ... (all 43 non-empty themes)
PASS  empty themes leave the drawing unchanged
FAIL  pre-registered PLANTUML_THEMES works without fetching themes.js
PASS  missing themes.js still renders the diagram, unthemed
FAIL  missing themes.js warns on the console
        no console warning mentions themes.js, so the page author gets no signal
```

(The second to last check passes vacuously there: an engine that ignores `!theme` entirely renders unthemed with or without `themes.js`.)

With this change all thirteen pass.

The third commit wires that into CI on push and pull request. Delete
`.github/workflows/browser-test.yml` if you would rather not spend the minutes; the checks
still run locally with the command above.

`ThemesJsTest` on the JVM side is narrower on purpose: it asserts that both generated
artifacts (`themes.js` and `ThemeList`) still match the `.puml` files on the classpath,
which is the ground truth under JUnit. It cannot exercise the browser branch, because
`TeaVM.isTeaVM()` is false there. What it does give is the other half of the argument:
the browser is handed the same bytes the desktop build reads, and runs them through the
same preprocessor, so desktop theme behaviour carries over. It was negative-tested by
changing one hex digit in `themes.js`, which fails it.

## Measurements

Built with `gradlew :plantuml-mit:npmPackage -Pci`, both before and after, and compared
in headless Chromium.

The same four `!theme` directives rendered by both builds:

**Before** (all four ignored):

![before: four themed diagrams all rendering with the default styling](https://raw.githubusercontent.com/lemonlion/plantuml/783f7ebdc9244b5e835a5138f1dceb5e52cdee45/themes-before.png)

**After**:

![after: each theme applies its own styling](https://raw.githubusercontent.com/lemonlion/plantuml/783f7ebdc9244b5e835a5138f1dceb5e52cdee45/themes-after.png)

Rendering `!theme <name>` on a two message sequence diagram, dominant fill colours:

| probe | before | after |
| --- | --- | --- |
| `!theme amiga` | `E2E2F0` `181818` (default) | `FFFFFF` `0B58A8` |
| `!theme hacker` | `E2E2F0` `181818` (default) | `D3F198` `151515` `B5E853` |
| `!theme cerulean` | `E2E2F0` `181818` (default) | `59B6EC` `FFFFFF` `2FA4E7` |
| `!theme zzz-does-not-exist` | renders normally, no error | error diagram |
| `%get_current_theme()` after `!theme amiga` | empty | `Amiga Workbench 1.x` |
| no `!theme` line (control) | `E2E2F0` `181818` | `E2E2F0` `181818` |
| `!$t = "hacker"` then `!theme $t` | `E2E2F0` `181818` (default) | `D3F198` `151515` `B5E853` |
| `!theme cerulean` inside `!if` | `E2E2F0` `181818` (default) | `59B6EC` `FFFFFF` `2FA4E7` |
| `!theme amiga from <archimate>` | renders normally, no error | error diagram |

Correctness: the SVG for `!theme amiga` is identical to the SVG produced by pasting
that theme's body into the diagram by hand, on the same build, apart from the embedded
source metadata.

Coverage: all 44 bundled themes render without error, and all 43 with a non-empty body
change the drawing relative to the unthemed diagram. `_none_` is deliberately empty and
correctly leaves it unchanged.

No regression: SHA-256 of the rendered `<svg>` for 22 corpus diagrams that do not use
themes is unchanged between the two builds.

Other flavors: running `sjpp.jar` with `define=JAVA8` over the modified sources strips
the new import, the TeaVM branch and `loadJsTheme`, leaving `ThemeUtils` exactly as it
is today, and leaves `TContext.executeTheme` working as before. The stripped
`ThemeUtils`, `TContext` and `ThemesJsGenerator` all compile at `--release 8`.
`gradlew test -Pci` is green.

## Scope

Only bundled themes are resolved in the browser. `!theme x from <lib>`,
`!theme x from https://...` and `!theme x from <local path>` need the stdlib channel,
`SURL` and the filesystem respectively, none of which are wired for TeaVM here. They
now return null, which surfaces as a normal "Cannot load theme" error rather than being
ignored. Bundled stdlib themes could be routed through
`PathSystem.getTeaVMStdlibInputStream` later if that is wanted.

Separately, and not caused by this change: the TeaVM SVG driver does not paint the
diagram background. The Java build writes `style="...background:#0B58A8;"` on the `<svg>`
plus a full-size rect; the browser build writes neither, for `!theme` and for a plain
`skinparam backgroundColor` alike, before and after this change. 21 of the 44 themes set a
page background in the Java build and 12 of those are non-white, so themes built around a
dark backdrop (amiga, blueprint, crt-amber, crt-green and others) still look wrong in the
browser once their colours are applied to elements but the backdrop is missing. That is
worth its own fix in the SVG driver; this change is a prerequisite for it mattering.
That fix is now up as #2849, stacked on this PR.

A page can also upgrade the engine without deploying `themes.js`, and that case
degrades instead of breaking: the theme cannot apply, so the diagram renders unthemed,
byte-identical to what the page shows today, and the engine emits a console warning
naming `themes.js`, which is where the page author will look. An unknown theme name in
a successfully loaded `themes.js` still reports an error like the desktop build, since
that is a typo in the diagram text rather than a deployment gap. The last two commits
are a RED and GREEN pair pinning both halves of that contract (the last two checks in
the listing above). The distinction matters because `TeaVmScriptLoader` resolves the
script relative to the document, not to the module, so a page that imports the engine
from a CDN while being served from its own origin does not get `themes.js` from the
CDN: it needs the file on its own origin, or `globalThis.PLANTUML_THEMES` registered
(importing `themes.js` as a module does this). That resolution behaviour is shared with
`emoji.js` and the stdlib bundles, and could be addressed separately by resolving those
URLs against `import.meta.url`.

Implementing the fallback also uncovered a pre-existing bug. When a theme is loaded,
`ReadLineWithYamlHeader` reads the first line of the theme file to see whether it starts
a YAML header. If the file has no lines at all, that first line is null and the check
crashed with a NullPointerException. The fallback treats a missing themes.js as an empty
theme, which is exactly that case, but the desktop build can already hit it today: a
completely empty local theme file crashes the same way. The GREEN commit adds the null
check, with a JVM unit test that reproduces the crash when the check is removed.



