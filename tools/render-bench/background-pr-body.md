Follow-up to #2848, now rebased onto master after its merge, so the diff is just the four commits of this fix (RED, GREEN, REFACTOR, plus edge-case pins from a later review pass).

- **#2848** (merged): `!theme` was silently ignored by the browser engine (#2847). It made the themes apply.
- **This PR:** even with themes applied, the browser engine never paints the document background. That bug also exists without themes (`skinparam backgroundColor` is ignored too), but themes made it visible: white-on-blue amiga renders white text on the host page. This is the fix promised in the Scope section of #2848.

## What happens today

The browser (TeaVM) engine never paints the diagram background. The Java build honours the merged `root.document` BackGroundColor, which is where both `skinparam backgroundColor` and a theme's background land, by writing `style="...background:#RRGGBB;"` on the `<svg>` plus a rectangle covering the whole drawing. The browser build emits neither.

With #2848 this became visible: 21 of the 44 bundled themes set a document background in the Java build, 12 of them non-white, and those themes apply their element colours but lose their backdrop. `!theme amiga` is white on blue, so on a white page its arrows and text vanish.

## Why

Everything downstream already existed. `SvgGraphicsTeaVM` takes a background color in its constructor and paints the style plus a full viewBox rectangle, with the same skip rules as `SvgGraphics.paintBackcolor`. It just never received the color: `PlantUMLBrowser.buildSvg` only asked `tb.getBackcolor()` on the TextBlock, where the document background never surfaces, so it always took the no-background branch.

## The change

`buildSvg` now falls back to the merged `root.document` style via `TitledDiagram.calculateBackColor()`, exactly where `ImageBuilder.styled()` gets it for the Java build, extracted as `getPaintableDocumentBackground`.

The diversion only happens for a background that should actually be painted, judged on the plain (light mapped) color so the decision is mode independent: transparent and the skin default of white keep painting nothing, in dark mode too, where the skin pairs white with a dark value but the page has always supplied its own backdrop. Falling through also keeps the historic WHITE and BLACK fallback as the contrast reference for automatic colors.

## Red, green, refactor

The commits after the #2848 base are the actual TDD sequence (plus a later commit pinning edge cases):

1. RED: `browser-test/check-background.js`, asserting the Java contract (background style plus full viewBox rectangle, verified against the viewBox dimensions) for `skinparam backgroundColor`, the `<style>` document form and two themes, and asserting the skips (default white, transparent, dark mode control) keep painting nothing. Against the engine as master builds it today (with #2848 merged), the four painting checks fail:

```
PASS  control renders
PASS  control paints no background (white is skipped)
FAIL  skinparam backgroundColor paints the background
        expected background #0B58A8, got style=null rect=null
FAIL  <style> document BackGroundColor paints the background
FAIL  !theme amiga paints its blue background
FAIL  !theme blueprint paints its background
PASS  skinparam backgroundColor transparent paints nothing
PASS  dark mode control renders
PASS  dark mode control paints no background
```

2. GREEN: the fix. The RED checks earned their keep twice during this step: an unconditional diversion and a diversion judged on the mode mapped color both made the dark mode control start painting a background it never painted before, and the dark check caught both.

3. REFACTOR: the resolution extracted into a named method with the reasoning as javadoc, and the new check wired into the browser-test workflow. All checks re-run unchanged.

## Verification

- `check-background.js`: 16 of 16 on the fixed engine (the 4 painting checks fail before it). A later review pass probed the edges and found no defect; the probes are committed as pins: dark mode keeps an explicit background while suppressing only the default, scale keeps the rectangle covering the viewBox, activity and class diagrams paint too, a skinparam after `!theme` overrides the theme background like the Java build, and a gradient degrades to its first color instead of erroring.
- `check-themes.js` from #2848 (now on master): still 13 of 13.
- The 22 corpus diagrams (default background) are byte-identical to the build without this fix, so nothing changed for diagrams that set no background.
- `gradlew test -Pci` green.

Before (engine from current master: themes apply but backgrounds are missing):

![before: themed diagrams with their page backgrounds missing](https://raw.githubusercontent.com/lemonlion/plantuml/3d4f5fb14e2c57c401f8d6949dc72f1bfda91b9a/background-before.png)

After:

![after: the same themes with their backgrounds painted](https://raw.githubusercontent.com/lemonlion/plantuml/3d4f5fb14e2c57c401f8d6949dc72f1bfda91b9a/background-after.png)

## Scope

Gradient backgrounds degrade to the gradient's first color (`HColorGradient.toColor` under TeaVM); the Java build renders a real gradient. Left as is here.



