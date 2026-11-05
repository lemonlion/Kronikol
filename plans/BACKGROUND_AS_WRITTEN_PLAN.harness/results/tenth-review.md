# The tenth independent review, narrow, of the ninth's fix

Given about 40 lines of code and not the plan, on the release's commit before its round, 2026-11-05: `selectRowChosen`, `selectRowKeep`, what the search opens, what ends a search, and the accent's new place. Its scripts are `probes/review-10/`. It ran pages it generated itself, on published 5.28.3 and on this cut, through the bundled Chromium, and then 260 random reader sequences against its reading of the intent.

It found four faults in the ninth review's fix, all new, all reproduced with real clicks, and it tried a fix for each on the page from outside. With those on, its pinned sequences held and the random sequences flagged nothing.

| | Fault | What became of it |
|---|---|---|
| 1 | A search landing on the row already shown opens sections there, and they carry. Click c, close Steps, search a word only c holds, click d, click b: Background and Steps closed on d and b on 5.28.3, both open here, and they stay open after emptying. The search chose no row, so nothing marked what it opened. | `selectRowSearchOpens`: before the search opens a closed section, what the row holds is kept as the reader's, and the row counts from there as one the search chose. `A_search_that_lands_on_the_row_shown_passes_on_none_of_what_it_opens_there`. |
| 2 | The reader's own opening counted as the search's. A word in two outlines chooses c and reveals nothing; open Background on c; narrow to one scenario; click d: open on 5.28.3, closed here. | The same function, which acts only for a section that was closed. `What_the_reader_opened_before_the_search_opened_the_rest_carries`. |
| 3 | Back to the same search reopens what the reader closed. The search lands on b, close Background, click a status filter, Back: stays closed on 5.28.3, reopens here. `reset_filters` ended the search on every address. | `forget_sections_revealed` moved to `clear_all_filters`, and to `apply_url_hash` only when the address's search differs from the field's. `Going_back_to_the_same_search_keeps_closed_what_the_reader_closed`. |
| 4 | Sub-steps drawn since the mark read as the reader's change. With a subtree over 2,048 characters, held until opened: open a composite and its nested one on a, click c, the search lands on b, open the composite there, click e: nested open on e on 5.28.3, closed here. | The comparison reads a step not yet drawn as closed. `Sub_steps_drawn_since_the_search_chose_the_row_are_no_change_of_the_readers`. |

Its second fix's guard, acting only for a section that was closed, later came out: M85 survived, as nothing a reader can tell needs it (`mutations-tenth.txt`).

## The same on 5.28.3, so in the plan's §11

After Clear All or Back the next emptying returns to the old row, even one the reader came back to by hand. A Steps section holding a failed step takes a carried close, hiding the failure. An address applied while the outline is hidden chooses no row. A click on the shown row, or a search then emptying, drops what an undrawn nested composite would take. If the first row lacks the section, a search opens it on b, and after emptying, clicking b then c shows it open on c.

## What it found fine

Hand carry past rows that fail or lack a section; held sections and failed composites keeping their own state; a chosen row taking nothing, and what the search opened there not carrying; reveal once, then afresh after emptying, Clear All and an address with no search; two outlines kept apart; an address at load; no page errors; a closed diagrams section drawing on a search; and the accent at 1400 and 390 px, 10 to 13 against a triangle at 15 and a status icon at 28, the same as a scenario's own steps. The separate section draws no accent, as on 5.28.3.

## What it did not drive

The flat table, and on-demand diagram mode.
