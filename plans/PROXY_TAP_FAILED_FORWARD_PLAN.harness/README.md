# PROXY_TAP_FAILED_FORWARD_PLAN harness

Each probe runs on its own with `dotnet run --file <probe>.cs` (the .NET 10 SDK; no repo build). The ones that name a
package version on their first line run against that published package.

## probe/ (written with the plan, 4.18.1)

- `probe.cs`: seven failure paths through a tap (refused, timed out, cut short, a throwing resolver, sink and decoder,
  disposal mid-forward). Output: `probe-output-4.18.1.txt`.
- `probe-network-path.cs`, `probe-otlp-network-path.cs`: request targets that named another host through each tap
  (section 3.5). Outputs beside them.
- `probe-audit.cs`: the plan audit's measurements on Windows and Linux: more targets on both taps, the `ConnectTimeout`
  shape, `HttpRequestError` per failure, a throwing `ActivityListener`, and the R0 rule itself.
- `probe-scheme.cs`: a `ForwardBaseUri` that is not `http` or `https`.

## r0/ (R0, released as 4.19.1)

- `red-on-4.19.0-windows.txt`: the new facts run against 4.19.0's code (the R0 change stashed).
- `mutate-r0.sh <worktree> <out>`: the seven mutations; results in `mutate-r0-windows.txt` and
  `mutate-r0-linux.txt`, and `mutate-r0-m5-linux.txt` for the reordering mutation once the ordinary-path case was in.
- `suites-linux*.txt`: the ProxyTap and Otlp suites on Linux (.NET 10.0.12, the sdk container): the first run, the
  rerun that showed the span fact failing 5 of 5 before its fix, and the final run.
- `uri.cs`: no origin-form target makes the forward URI throw (section 4.7).
- `absform.cs`: who answers an absolute-form target naming another host on http.sys (section 3.5).
