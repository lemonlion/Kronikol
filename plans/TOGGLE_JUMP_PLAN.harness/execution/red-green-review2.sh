#!/bin/sh
# The review's facts red on the first fix (S/fix227), then the whole class green on the second (S/fix227-v5).
S=C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/6a05fcc2-08b1-4c23-8f2f-1be8814e2bd4/scratchpad
R=C:/Code/Kronikol-227/src/Kronikol/Reports
cd C:/Code/Kronikol-227 || exit 1
C=Kronikol.Tests.EndToEnd.ReadersPlaceOnRedrawTests
RED="FullyQualifiedName~$C.Headers_keeps_the_place_read_in_a_diagram_that_draws_the_words_every_object_has"
for f in A_place_taken_out_of_the_layout_while_a_toggle_draws_scrolls_the_page_no_more \
         A_second_toggle_after_the_page_jumped_keeps_the_place_read_at_the_second \
         Steps_keeps_the_place_read_in_a_diagram_zoomed_out_after_the_window_changed_width \
         A_wheel_across_a_zoomed_diagram_while_a_toggle_draws_keeps_the_place_read \
         A_tap_while_a_toggle_draws_keeps_the_place_read \
         Headers_keeps_the_text_read_in_place_on_a_page_that_scrolls_smoothly \
         A_scroll_that_leaves_the_place_on_its_line_lets_nothing_go \
         A_scroll_between_the_component_diagrams_picture_and_its_slider_stands \
         A_filter_that_takes_rows_away_keeps_the_place_read_among_notes_of_the_same_words; do
  RED="$RED|FullyQualifiedName~$C.$f"
done
RED="$RED|FullyQualifiedName~Kronikol.Tests.EndToEnd.PartsZoomDrawingTests.A_notch_far_down_a_diagram_in_parts_keeps_the_point_under_the_pointer_while_the_parts_above_grow"

cp $S/fix227/context-menu-script.js $S/fix227/collapsible-notes-script.js $S/fix227/report-components-script.js $R/
dotnet build tests/Kronikol.Tests.EndToEnd -c Debug -nologo -v:q > $S/logs/build-red-review2.log 2>&1 || { echo "red build failed"; }
dotnet test tests/Kronikol.Tests.EndToEnd --no-build -c Debug --filter "$RED" --logger "console;verbosity=normal" > $S/logs/test-red-review2.log 2>&1
echo "red run exit $?"

cp $S/fix227-v5/context-menu-script.js $S/fix227-v5/collapsible-notes-script.js $S/fix227-v5/report-components-script.js $R/
for f in context-menu-script.js collapsible-notes-script.js report-components-script.js; do
  cmp -s $S/fix227-v5/$f $R/$f || echo "NOT restored: $f"
done
dotnet build tests/Kronikol.Tests.EndToEnd -c Debug -nologo -v:q > $S/logs/build-green-v5.log 2>&1 || { echo "green build failed"; exit 1; }
dotnet test tests/Kronikol.Tests.EndToEnd --no-build -c Debug \
  --filter "FullyQualifiedName~$C|FullyQualifiedName~Kronikol.Tests.EndToEnd.PartsZoomDrawingTests" \
  --logger "console;verbosity=normal" > $S/logs/test-green-v5.log 2>&1
echo "green run exit $?"
