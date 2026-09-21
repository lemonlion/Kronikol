# Support `!theme` in the browser (TeaVM) build

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

The strongest of the twelve checks is that loading a theme by name produces exactly what
pasting that theme's body into the diagram produces. Both sides are rendered by the same
engine, so only the loading path differs.

Pointed at an engine built before this change, 8 of the 12 fail:

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
FAIL  missing themes.js reports an error rather than rendering unthemed
```

With this change all twelve pass.

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

(Screenshots attached below the tables: the same four `!theme` directives rendered by
both builds, side by side.)

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

One behaviour change worth stating plainly: a page that upgrades the engine without
deploying `themes.js` alongside it turns previously silent `!theme` lines into error
diagrams. `themes.js` is included in the npm package, and its README now says it must be
served next to the engine, for that reason. Note that `TeaVmScriptLoader` resolves the
script relative to the document, not to the module, so a page that imports the engine
from a CDN but is served from its own origin needs `themes.js` on that origin or needs
to register `globalThis.PLANTUML_THEMES` (importing `themes.js` as a module does this).
This is the existing behaviour for `emoji.js` and the stdlib bundles too, and could be
addressed separately by resolving those URLs against `import.meta.url`.
