# S5's diagram API, B10, B12, B13 and B16: red on the base, then green

The base is `5755f5dc` on `feat/github-search-diag`, in the worktree `C:/Code/Kronikol-search-diag` (LF checkout). Each
fact was written first and run there before any of the code it covers changed: the E2E facts on Playwright's bundled
Chromium (B16's on the bundled WebKit 26.4), the Jint facts in `tests/Kronikol.Tests.SearchEngine`. One run of the 16
E2E facts together, filtered by name: `Failed: 16, Passed: 0`.

## The API's pure part: `DiagramLabelTests` (Jint)

`labelOf(statement)`, the reading of a message statement's label that `findMessage` compares each drawn message with.
The page script `collapsible-notes-script.js` loads in Jint as `DiagramContextMenu.GetCollapsibleNotesScript()` gives
it; there is no `window.kronikol_diagrams` on the base.

```
Failed!  - Failed:    31, Passed:     0, Skipped:     0, Total:    31
A_message_statement_s_label_reads_as_the_engine_draws_it (17 rows)  DiagramLabelTests.cs:51
A_line_that_is_no_message_has_no_label (14 rows)                    DiagramLabelTests.cs:68
Jint.Runtime.JavaScriptException : Cannot read property 'labelOf' of undefined
```

## The API: `DiagramApiTests` (E2E)

Eleven facts fail at their first call into the API, which the base does not have:

| Fact | Line | Red |
|---|---|---|
| `WhenDrawn_waits_for_a_diagram_brought_near_and_is_kept_once_every_part_is_drawn` | :115 | `TypeError: Cannot read properties of undefined (reading 'whenDrawn')` |
| `WhenDrawn_gives_up_after_its_timeout_for_a_diagram_never_brought_near` | :149 | the same, `whenDrawn` |
| `WhenDrawn_after_show_waits_for_the_redraw_show_queued` | :167 | the same, `show` |
| `WhenDrawn_is_kept_for_a_flame_view_once_its_bars_are_drawn` | :189 | the same, `whenDrawn` |
| `Show_turns_on_a_scenario_s_states_with_one_redraw_and_leaves_the_report_and_other_scenarios_alone` | :209 | the same, `show` |
| `Show_reaches_a_diagram_the_scenario_draws_later` | :251 | the same, `show` |
| `OpenNote_while_another_diagram_draws_waits_and_then_draws_the_note_in_that_state` | :282 | the same, `openNote` |
| `OpenNote_before_a_first_draw_seeds_the_note_and_the_diagram_is_drawn_once` | :337 | the same, `openNote` |
| `FindNote_finds_a_note_by_its_original_index_in_a_later_part` | :376 | the same, `findNote` |
| `FindMessage_finds_the_nth_message_whose_label_reads_as_the_statement_s` | :404 | the same, `findMessage` |
| `FindNote_and_findMessage_find_the_element_meant_through_filters_note_states_and_parts` | :459 | the same, `findMessage` |

The twelfth runs only the page's own note button, so it fails on the behaviour, not on a missing name:

```
Failed A_note_button_pressed_while_another_diagram_draws_is_not_lost [32.8 s]
System.TimeoutException : Timeout 30000ms exceeded.
at DiagramApiTests.cs:line 324
```

The note's minus button was clicked while `window._plantumlRendering` was set (another diagram drawing), and
`setNoteState` returned at its first line, `if (window._plantumlRendering || container._noteRendering) return;`: the
press was dropped, so the note never went to one row after the flag was released.

The plan's H28 (`The_original_to_drawn_line_map_holds_through_filters_note_states_and_parts`, Jint
`NoteSourceMapTests`) is the last row above instead: the API keeps no map of lines (see the report of this stage), so
the fact is that `findNote` and `findMessage` find the element meant through filters, note states and parts.

## B10: `BackForwardRestoreTests.Back_returns_to_the_diagram_it_left_after_a_diagram_above_it_draws`

```
Failed Back_returns_to_the_diagram_it_left_after_a_diagram_above_it_draws [4.0 s]
Assert.InRange() Failure: Value not in range
Range:  (-2 - 2)
Actual: 1020.28125
at BackForwardRestoreTests.cs:line 526
```

The page was left with the second participant head of "3 Diagram below" under the bar; the diagram of "2 Diagram above"
was then drawn, and Back put that head 1020 px from where it was. NAV's `address()` saved the head's id, `part1-head`,
and every drawn diagram has a `part1-head`: the first in the page, the one above, was the one found.

## B12: `ScenarioSettingsTests.A_scenario_s_Payloads_and_Headers_reach_its_diagrams_drawn_later`

```
Failed A_scenario_s_Payloads_and_Headers_reach_its_diagrams_drawn_later [3.0 s]
Assert.Contains() Failure: Sub-string not found
String:    "CallerOrdersAPICallerOrdersAPICallerOrdersAPI1GET:"···
Not found: "Content-Type=application/json"
at ScenarioSettingsTests.cs:line 261
```

The outline's gear set Payloads to Expanded and turned Headers on while only row 1's diagram was drawn; row 2's diagram,
drawn when its row was picked, read the report's choices (headers hidden), not the scenario's.

## B13: `NoteYamlToggleTests.A_collapsed_YAML_note_previews_its_YAML`

```
Failed A_collapsed_YAML_note_previews_its_YAML [1.5 s]
Assert.Contains() Failure: Filter not matched in collection
Collection: ["OrderService", "Caller", "Given an order request", "POST /api/orders", "\"id\": 9007199254740993, \"query\": \"SELECT o.id"···, ···]
at NoteYamlToggleTests.cs:line 291
```

A note shown as YAML and collapsed by its minus button previewed its JSON: `getNotePreview` was handed the note blocks
parsed from the source before the YAML pass.

## B16: `LongPayloadScrollTests.A_scaled_diagram_s_scroll_box_is_painted_inside_its_card` (WebKit)

```
Failed A_scaled_diagram_s_scroll_box_is_painted_inside_its_card [5.0 s]
the box painted 0 pixels in its card
at LongPayloadScrollTests.cs:line 1515
```

At a zoom under 75 %, hiding the note's scroll box changed no pixel inside its card: WebKit paints a scrollable box in a
`foreignObject` at its unscaled size and place, so the box was drawn below and right of the card it belongs to.

## Found on the way

### B16 had a second cause: `A_scroll_box_lies_inside_its_card_in_WebKit`

With the box drawn at the picture's scale, B16's fact still failed. A probe found the box starting at the picture's left
edge: the engine draws a line's carriage return (the source's lines end in CRLF) as a `<text>` of its own, WebKit gives a
text that paints nothing a box at 0,0, and `scrollBoxPlace` took the leftmost text's x. The fact on the geometry WebKit
reports, written first:

```
Failed A_scroll_box_lies_inside_its_card_in_WebKit
the box reaches out of its card by -363.7, 2, 17.5, 10.5 px (left, right, top, bottom)
at LongPayloadScrollTests.cs:line 1548
```

`scrollBoxPlace` now reads a text's box only when it has a width or a height (`inkBox`).

### A refused drawing was reported as drawn: `OpenNote_whose_drawing_the_engine_refuses_leaves_the_note_and_its_picture_as_they_were`

Written after the API, with `window.plantuml.render` throwing once; it failed on the first run:

```
Failed OpenNote_whose_drawing_the_engine_refuses_leaves_the_note_and_its_picture_as_they_were
Expected: "false,true,true,true"
Actual:   "true,true,true,true"
at DiagramApiTests.cs:line 495
```

The note and its picture were given back, but `openNote` was kept with true: the batch handed `processRenderQueue` its own
list of items, which the queue empties as it draws, so asking the batch afterwards which drawing failed found no items.
The queue now gets a copy.

### The note buttons' busy mark: `ToggleButtonPendingTests.Clicked_scenario_button_is_pending_until_its_rerender_completes`

With the gear's buttons moved onto `show`, the first shape of the batch marked its controls busy only when it was drawn,
a microtask after the click, and the fact failed (`the clicked button must be marked pending as soon as the re-render
starts`). `redrawLater` marks them as it takes them.

### whenDrawn was kept before a wide note's last drawing: `OpenNote_draws_a_note_wide_before_and_after_a_first_draw_and_whenDrawn_waits_for_its_last_drawing`

`openNote` takes a width as well as a state and a format (the plan's took the two). The fact for it, written after the
API, failed 3 runs of 3:

```
Failed OpenNote_draws_a_note_wide_before_and_after_a_first_draw_and_whenDrawn_waits_for_its_last_drawing
Expected: "0 drawn after, 0 drawn after"
Actual:   "2 drawn after, 2 drawn after"
at DiagramApiTests.cs:line 430
```

The note was drawn wide on both diagrams, the one drawn and the one not drawn yet, but `whenDrawn` was kept before two
more pictures: a diagram with a wide note is drawn again once painted, by the pass that fits it to its column
(`refineNoteWidth`), and that pass waits for the drawing to end in a timer of its own, which nothing counted. `isDrawn`
now counts it (`_refineWaiting`): green 3 runs of 3.

### A note's own redraw now decorates a far diagram as it comes near: `DiagramNoteSplitTests.Three_diagram_split_dblclick_on_continuation_note_cycles_state`

Red on this branch's code, green on the base:

```
Failed Three_diagram_split_dblclick_on_continuation_note_cycles_state
puml-2 should have hover rects and toggle icons after dblclick state cycle
at DiagramNoteSplitTests.cs:line 283
```

A probe in the fact: the diagram double-clicked started at 1649.7 px in a window 1,080 px high, with `_kDecorate` set. A
note's redraw goes through the render queue now, whose rule since 5.5.1 gives a diagram near the window its buttons once
the new picture is painted and one far from it as it comes near; `rerenderWithNoteStates` decorated at once, wherever
the diagram was. No reader double-clicks a note 570 px below the window, and a pointer reaching an undecorated diagram
decorates it first (CN's `mouseover` listener). The fact now brings the note into view before the double-click, and
waits for the drawing to end as the class's own `WaitForSvgReRender` does (with `PollingInterval = 200`, which its wait
lacked): green.

### A fact that could not see what it read

`The_report_s_Payloads_and_Headers_take_over_a_scenario_s_own_for_its_diagrams_drawn_later` first read every text of the
picture for the headers and failed with `"1 row, headers"`: a folded note's `<title>`, its tooltip, holds its headers
whatever is drawn. It reads the painted `<text>` elements alone, as the B12 fact now does. Its first shape (the gear's
Expanded, the report's Collapsed) then survived the mutation that keeps a scenario's Headers after the report's (see
below): a folded note paints no headers either way. It now goes the other way round (the gear's Collapsed, the report's
Expanded) and expects the note whole, 14 rows, and no headers.

## The edge facts, written once the first facts were green

| Fact | First run |
|---|---|
| `DiagramApiTests.WhenDrawn_is_kept_with_false_once_its_signal_aborts` | green (its mutation below is red) |
| `DiagramApiTests.Show_and_openNote_in_one_task_draw_a_diagram_once` | green (its mutation below is red) |
| `DiagramApiTests.OpenNote_draws_a_note_of_a_later_part_in_that_state` | green |
| `DiagramApiTests.OpenNote_is_kept_with_false_for_a_note_the_diagram_does_not_have_and_a_view_its_payload_has_not` | green (its mutation below is red) |
| `DiagramApiTests.OpenNote_whose_drawing_the_engine_refuses_leaves_the_note_and_its_picture_as_they_were` | red, a bug (above) |
| `DiagramApiTests.OpenNote_draws_a_note_wide_before_and_after_a_first_draw_and_whenDrawn_waits_for_its_last_drawing` | red, a bug (above) |
| `ScenarioSettingsTests.The_report_s_Payloads_and_Headers_take_over_a_scenario_s_own_for_its_diagrams_drawn_later` | red, the fact's own fault (above) |
| `LongPayloadScrollTests.A_scroll_box_lies_inside_its_card_in_WebKit` | red, B16's second cause (above) |
| `LongPayloadScrollTests.A_wheel_over_a_scaled_diagram_s_scroll_box_scrolls_the_box` | green, a guard (below) |
| `LongPayloadScrollTests.A_scaled_diagram_s_scroll_box_takes_a_click_and_the_keys_in_WebKit` | green, a guard (below) |

## Each fix taken out again (mutations)

Each line of the fix below was taken out, or put back as the base had it, the test project built, and the fact meant to
catch it run alone. The source was put back after each.

| Taken out | Fact | Red |
|---|---|---|
| B10: `address()` returns no address inside a drawn diagram | `Back_returns_to_the_diagram_it_left_after_a_diagram_above_it_draws` | `Actual: 1020.28125` (range -2 to 2) |
| B12: the first draw reads the report's Payloads (`window._detailsDefault`) | `A_scenario_s_Payloads_and_Headers_reach_its_diagrams_drawn_later` | `Not found: "silver_14"` |
| B12: the first draw reads the report's Headers (`window._headersHidden`) | the same | `Not found: "Content-Type=application/json"` |
| B13: the preview cut from the original's note blocks | `A_collapsed_YAML_note_previews_its_YAML` | the base's red, the JSON line previewed |
| B16: no transform in WebKit (`BOX_SCALED_BY_HAND` false) | `A_scaled_diagram_s_scroll_box_is_painted_inside_its_card` | `the box painted 0 pixels in its card` |
| The batch drawn at once, not after the task | `Show_and_openNote_in_one_task_draw_a_diagram_once` | `Expected: 1, Actual: 2` (swaps) |
| The report's Payloads leave the scenarios' own choices | `The_report_s_Payloads_and_Headers_take_over_...` | `Actual: "1 rows, no headers"` |
| The report's Headers leave the scenarios' own choices | the same | `Actual: "16 rows, headers"` (in its first shape it passed) |
| `whenDrawn` ignores its signal | `WhenDrawn_is_kept_with_false_once_its_signal_aborts` | `Actual: "false after its timeout"` |
| A failed drawing gives the note's state back no more | `OpenNote_whose_drawing_the_engine_refuses_...` | `Actual: "false,false,true,true"` |
| `openNote` does not check that a payload has a YAML view | `OpenNote_is_kept_with_false_for_a_note_the_diagram_does_not_have_and_a_view_its_payload_has_not` | `Actual: "false,false,true,true,false"` |
| A seed not applied at the first draw | `OpenNote_before_a_first_draw_seeds_the_note_and_the_diagram_is_drawn_once` | `Actual: "2 json yaml"` (expected `"0 json yaml"`) |
| The controls marked busy at the flush, not at the click | `ToggleButtonPendingTests.Clicked_scenario_button_is_pending_until_its_rerender_completes` | `the clicked button must be marked pending as soon as the re-render starts` |

Two pieces of B16 were tried and dropped, as nothing failed without them: forwarding the wheel from the note's hover
rectangle to the box, and a guard on a click there. WebKit's `elementFromPoint` finds the note's hover rectangle over a
box in a scaled picture, with the transform or without it, yet a real wheel, a click and the keys reach the box. Two
facts stay as guards on that, green with and without either piece: `A_wheel_over_a_scaled_diagram_s_scroll_box_scrolls_the_box`
and `A_scaled_diagram_s_scroll_box_takes_a_click_and_the_keys_in_WebKit`.

## Green

On the branch's code, the E2E facts on Playwright's bundled Chromium (the WebKit facts on its bundled WebKit 26.4, the
cross-engine ones on its Firefox too). Only the classes the change reaches were run, never a whole suite:

- `DiagramLabelTests` (Jint, `tests/Kronikol.Tests.SearchEngine`): 45 of 45.
- `tests/Kronikol.Tests`, the 18 classes that read the changed scripts (`DiagramContextMenuTests`,
  `ScenarioTruncateAndCollapsibleNotesTests`, `NoteAppearanceScriptTests`, `NoteFormatToggleScriptTests`,
  `PayloadScrollLinesTests`, `NoteCopyFidelityScriptTests`, `EventsAndSetupToggleReportTests`,
  `ToggleDefaultsMarkupTests`, `CaptureFootnoteTests`, `StepBarPlantUmlTests`, `BackNavigationReportTests`,
  `FailureClusterReportTests`, `FocusScriptTests`, `CopyScenarioNameReportTests`, `StylesheetRulesTests`,
  `VerdictFencesTests`, `ThemedPagesHtmlTests`, `ReportSheetColoursTests`): 397 of 397.
- E2E, the note, toggle and Back classes (`ToggleButtonPendingTests`, `NoteButtonIndexTests`, `NoteButtonLayoutTests`,
  `NoteButtonsAfterHeaderHideTests`, `NoteCollapseAfterFilterTests`, `NoteAppearanceTests`, `NoteWidthFitTests`,
  `NoteExpandArrowHeaderHiddenTests`, `NoteLengthInYamlViewTests`, `NotePreviewBraceTests`, `NoteFormatDropdownTests`,
  `NoteFormatDefaultTests`, `FilterToggleModelTests`, `DatabaseToggleTests`, `EventsAndSetupToggleTests`,
  `AssertionToggleTests`, `ToggleDefaultsTests`, `DiagramNoteStepsToggleTests`, `DiagramNoteLongNoteTests`,
  `DiagramNoteSplitTests`, `CappedResponseNoteRenderingTests`, `NoteContextMenuIndexTests`, `BackForwardPlacesTests`,
  `BackForwardFilterTests`, `BackForwardLayerTests`, `LongPayloadScrollTests`): 278 of 281.
- E2E, the classes that draw diagrams again or read the toolbar (`CapturedTextEscapeTests`, `DiagramNoteBasicTests`,
  `DiagramZoomTests`, `FilterStateDefaultTests`, `FittingDiagramZoomTests`, `FormStateRestorationTests`,
  `HeadersDetailsInterferenceTests`, `KafkaNoteTests`, `LinedUpPartsTests`, `NoteFormatCardColourTests`,
  `NoteHeaderFormsTests`, `ReportToolbarTests`, `SpecificationsToggleInheritanceTests`, `SplitDiagramZoomTests`,
  `ThreeFragmentContinuationTests`, `ToolbarStylingTests`, `ZoomKeptOnRedrawTests`, `ZoomRobustnessTests`,
  `DeepSearchTests`): 230 of 235.
- E2E, this stage's classes and the queue's (`DiagramApiTests`, `ScenarioSettingsTests`, `NoteYamlToggleTests`,
  `BackForwardRestoreTests`, `NoteRedrawFailureTests`, `NoteRedrawQueueTests`, `ZoomNoteInteractionTests`,
  `ContinuationNoteInteractivityTests`, `AnimationFrameWaitTests`, `BrowserStallTests`): 105 of 111.
- After the last two fixes above: `DiagramApiTests` (18), `DiagramNoteSplitTests`, `ZoomRobustnessTests` and
  `ScenarioSettingsTests`, 66 of 66.

Every failure in those runs is accounted for: the split fact above (fixed), one fact that failed once under load, and the
facts below, which fail on the base as well. `ZoomRobustnessTests.A_diagram_in_parts_a_hair_wider_than_its_box_fits_it_with_no_scrollbar_and_a_redraw_leaves_the_scroll_alone`
failed once in the second batch (`Expected: 0, Actual: 1`, scroll anchoring held once, :480), and passed alone 2 runs of
2 on this code, on the base, and in the last run.

## Red before this work, and still red

Twelve facts fail on this branch and fail the same with the base's `collapsible-notes-script.js` and
`report-navigation-script.js` (from `5755f5dc`) put back and built:

```
BackForwardRestoreTests.R6_Without_tabs_a_chip_and_a_link_button_go_back_as_they_do_with_them    Expected: 0, Actual: 1   :167
BackForwardRestoreTests.R11_In_an_iframe_srcdoc_..._Back_undoes_a_filter (chromium, firefox, webkit)
                                                     waiting for Locator(".filtering-box .more-filters")   :340
BackForwardRestoreTests.R13_In_WebKit_a_hundred_and_fifty_clicks_on_one_chip_and_a_held_arrow_key_throw_nothing
                                                     the same locator   :388
BackForwardRestoreTests.R14_The_search_writes_at_once_on_Enter_so_Back_right_after_it_undoes_the_search
                                                     Back right after Enter: Back left the report   :459
BackForwardFilterTests.F2_A_word_typed_in_the_search_is_one_entry    Timeout 3000ms exceeded   :63
BackForwardFilterTests.F4_Back_after_Clear_All_puts_every_filter_back    Locator(".nav-clear") never visible   :102
ToolbarStylingTests.A_tapped_filter_on_a_phone_keeps_its_active_colour (two rows)
                                                     waiting for Locator(".mobile-filter-toggle")   :308
DeepSearchTests.Deep_results_join_after_the_instant_ones_of_their_strength_while_the_bar_runs
                                                     Expected "List orders quietly", Actual "Create order successfully"   :547
ZoomKeptOnRedrawTests.A_diagram_zoomed_again_without_a_scroll_keeps_its_left_edge_when_drawn_again_while_not_displayed
                                                     Expected: 0, Actual: 190 (before any redraw)   :419
```

None but the last reads a diagram. R11, R13, F4 and the two `ToolbarStylingTests` rows look for the filter box's
controls as they were before S6 (`.more-filters`, the phone's own `.mobile-filter-toggle`, a Clear All shown without
opening the box); R6 expects no tabs on a page with nothing beside its features, which has the Search tab now; F2 and
R14 wait for the address the search wrote before S2 to S4; `DeepSearchTests` is the search's own. The last fails at a
zoom of the slider alone, before anything is drawn again. They are left to the stages that changed those, so the
branches merge clean.
