**Describe the bug**
In the browser (TeaVM) engine, `!theme <name>` is accepted and then silently ignored. No styling is applied and no error is reported. An unknown theme name (`!theme zzz-does-not-exist`) behaves exactly like a real one, so a typo is invisible too.

The engine also advertises the themes it cannot apply: `%get_all_theme()` returns all 44 bundled names in the browser (#2725 made it TeaVM aware), while `%get_current_theme()` returns `{}` after a `!theme` line naming one of them.

**To Reproduce**
Steps to reproduce the behavior:

1. Render this diagram with the browser engine (`@plantuml/core` on npm, or a `plantuml.js` built from master), for example in the playground `index.html` shipped with the package:

```
@startuml
!theme amiga
Alice -> Bob: hello
Bob --> Alice: hi
@enduml
```

2. Compare with the same diagram without the `!theme` line.
3. The two rendered SVGs are identical apart from the embedded `<?plantuml-src ?>` metadata: default `#E2E2F0` participants instead of the amiga theme's `#0B58A8` on `#FFFFFF`.

**Expected behavior**
Same behaviour as the desktop build: the theme is applied, and an unknown theme name reports an error.

**Screenshots**
Four different `!theme` directives, all rendering identically unthemed:

![four themed diagrams all rendering with the default styling](https://raw.githubusercontent.com/lemonlion/plantuml/783f7ebdc9244b5e835a5138f1dceb5e52cdee45/themes-before.png)

**Desktop (please complete the following information):**

- OS: any (verified on Windows 11; the cause is in the compiled engine, not the host)
- Browser: any (verified on Chromium headless)
- Version: `@plantuml/core` up to and including 1.2026.7, and current master (8c574c5)

**Additional context**
Two causes, both browser only:

1. `TContext.executeTheme` has its whole body inside `if (!TeaVM.isTeaVM())`, so the directive is a no-op there. Since `isTeaVM()` is a `@PlatformMarker` the body is also dead-code eliminated, which is why no theme content appears anywhere in the compiled `plantuml.js`.
2. `ThemeUtils.loadBundledOrLocalTheme` reads `/themes/puml-theme-<name>.puml` through `getResourceAsStream`, and the browser build has no classpath, so the lookup could not succeed even if the code path were live.

Nothing else is missing: the bundled themes are plain preprocessor sources (no `!include`, no sprites), and pasting the body of `puml-theme-amiga.puml` directly into a diagram renders correctly in the browser today. Only the delivery of the theme text to the preprocessor is missing.

Two PRs address this:

- PR #2848 fixes both halves of this issue: it removes the TeaVM guard so the directive actually executes, and it delivers the theme texts to the browser so the load can then succeed (a generated `themes.js`, fetched on demand the same way `emoji.js` and `openiconic.js` already are).
- PR #2849 (stacked on #2848) fixes a second bug that applying themes makes visible: the browser engine never paints the document background, so a white-on-blue theme like amiga would render white text on the host page. That bug also exists without themes (`skinparam backgroundColor` is ignored too), but it only starts to matter once themes work.
