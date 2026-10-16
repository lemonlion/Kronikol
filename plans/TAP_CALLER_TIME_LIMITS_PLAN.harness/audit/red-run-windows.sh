#!/bin/sh
# The patch's facts on the red tree (4.26.1 with the seams stubbed), on Windows. $1 = the red tree, $2 = the output folder.
RED="$1"
OUT="$2"
cd "$RED" || exit 1
dotnet test tests/Kronikol.Tests.ProxyTap/Kronikol.Tests.ProxyTap.csproj --no-build --logger "console;verbosity=normal" \
  --filter "FullyQualifiedName~StallWatchTests.A_watch_made_while|FullyQualifiedName~StallWatchTests.A_check_that_blocks|FullyQualifiedName~ProxyTapLifecycleTests|FullyQualifiedName~ProxyTapStoppingTests.A_request_taken_but_not_yet_handled|FullyQualifiedName~ProxyTapStallTests.A_caller_that_stalls_in_its_body|FullyQualifiedName~ProxyTapStallTests.A_caller_that_stops_reading_its_answer|FullyQualifiedName~ProxyTapStallTests.A_stall_the_tap_cuts|FullyQualifiedName~ProxyTapTests.A_head_answer_keeps" \
  > "$OUT/red-4.26.1-ProxyTap-windows.txt" 2>&1
dotnet test tests/Kronikol.Tests.TcpTap/Kronikol.Tests.TcpTap.csproj --no-build --logger "console;verbosity=normal" \
  --filter "FullyQualifiedName~TcpTapStallTests.A_start_that_cannot_bind|FullyQualifiedName~TcpTapStallTests.A_pair_keeps_one_write_entry|FullyQualifiedName~TcpTapStallTests.A_half_closed_pair_whose_client_stops_reading|FullyQualifiedName~TcpTapStallTests.A_pair_the_reaper_and_a_limit|FullyQualifiedName~TcpTapStallTests.A_limit_that_cuts_a_reply_short|FullyQualifiedName~TcpTapStallTests.The_stop_line_names_the_connections_the_reaper_closed" \
  > "$OUT/red-4.26.1-TcpTap-windows.txt" 2>&1
dotnet test tests/Kronikol.Tests.Otlp/Kronikol.Tests.Otlp.csproj --no-build --logger "console;verbosity=normal" \
  --filter "FullyQualifiedName~HttpRequestReaderTests.A_head_of_one_header|FullyQualifiedName~HttpRequestReaderTests.An_empty_line|FullyQualifiedName~OtlpTapRefusalTests.A_transfer_coding|FullyQualifiedName~OtlpTapStallTests.A_request_arriving_as|FullyQualifiedName~OtlpTapStallTests.A_refused_head_whole|FullyQualifiedName~OtlpTapStallTests.An_empty_line_after|FullyQualifiedName~OtlpTapStallTests.A_start_that_cannot_bind|FullyQualifiedName~OtlpTapLogTests.A_throwing_log_callback" \
  > "$OUT/red-4.26.1-Otlp-windows.txt" 2>&1
for f in ProxyTap TcpTap Otlp; do
  echo "== $f"
  grep -E "^\s+(Passed|Failed) Kronikol|Passed!|Failed!" "$OUT/red-4.26.1-$f-windows.txt" | sed 's/^ *//' | cut -c1-200
done
