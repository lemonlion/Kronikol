#!/bin/bash
# R1/R2 mutations (plans/PROXY_TAP_FAILED_FORWARD_PLAN.md section 6): each must make at least one fact fail.
# Usage: mutate-r1r2.sh <worktree> <outfile> [filter]
W=$1; OUT=$2; FILTER=${3:-ProxyTapFailure}; cd "$W" || exit 1
: > "$OUT"
T=src/Kronikol.Extensions.ProxyTap/ProxyTap.cs; F=src/Kronikol.Extensions.ProxyTap/ForwardFailure.cs
run() { # name file perl-expr
  local name=$1 file=$2 expr=$3
  cp "$file" /tmp/mut171.bak
  perl -0pi -e "$expr" "$file"
  if cmp -s "$file" /tmp/mut171.bak; then echo "$name: SUBSTITUTION DID NOT APPLY" >> "$OUT"; return; fi
  local res; res=$(timeout 900 dotnet test tests/Kronikol.Tests.ProxyTap --filter "FullyQualifiedName~$FILTER" 2>&1 | grep -E "Failed!|Passed!|error CS" | head -3)
  cp /tmp/mut171.bak "$file"
  if echo "$res" | grep -q "error CS"; then echo "$name: DOES NOT COMPILE ($res)" >> "$OUT"
  elif echo "$res" | grep -q "Failed!"; then echo "$name: KILLED ($(echo "$res" | grep -oE 'Failed: +[0-9]+, Passed: +[0-9]+'))" >> "$OUT"
  else echo "$name: SURVIVED ($res)" >> "$OUT"; fi
}
run "M1 no span end on a failure" $T 's/            EndSpan\(client, restore: server \?\? outer\);\n            server\?\.SetTag\("http\.response\.status_code", 502\);\n            server\?\.SetStatus\(ActivityStatusCode\.Error\);\n            EndSpan\(server, restore: outer\);\n/            server?.SetTag("http.response.status_code", 502);\n            server?.SetStatus(ActivityStatusCode.Error);\n/'
run "M2 failed response recorded with no content" $T 's/badGateway, null, DateTimeOffset\.UtcNow, error\)/[], null, DateTimeOffset.UtcNow, error)/'
run "M3 TimedOut without the tap's words" $F 's/ForwardFailureKind\.TimedOut => \$"no answer from [^\n]*\n/ForwardFailureKind.TimedOut => chain,\n/'
run "M4 Stopping counted as a forward failure" $T 's/if \(failure == ForwardFailureKind\.Stopping\)/if (false)/'
run "M5 recorded failure added to RequestsCaptured" $T 's/Interlocked\.Increment\(ref _failuresRecorded\);/{ Interlocked.Increment(ref _failuresRecorded); Interlocked.Increment(ref _captured); }/'
run "M6 502 body escaped with the default encoder" $T 's/JavaScriptEncoder\.UnsafeRelaxedJsonEscaping/JavaScriptEncoder.Default/'
run "M7 resolver exception rethrown" $T 's/CaptureFault\("the IdentityResolver", ex\);/throw;/'
run "M8 record catch removed" $T 's/CaptureFault\("the sink", ex\);\n            return false;/throw;/'
run "M9 InvalidOperationException dropped from TryDecodeBody" $T 's/DecoderFallbackException or IOException or InvalidOperationException\)/DecoderFallbackException or IOException)/'
run "M10 Classify without its OperationCanceledException test" $F 's/        if \(exception is not OperationCanceledException\)\n            return ForwardFailureKind\.Failed;\n//'
run "M11 ConnectTimedOut folded into Failed" $F 's/return ForwardFailureKind\.ConnectTimedOut;/return ForwardFailureKind.Failed;/'
run "M12 error.type always the type name" $F 's/public static string ErrorType\(Exception exception\) => \(exception as HttpRequestException\)\?\.HttpRequestError switch/public static string ErrorType(Exception exception) => exception.GetType().FullName!;\n\n    private static string Unused(Exception exception) => (exception as HttpRequestException)?.HttpRequestError switch/'
run "M13 span start guard removed" $T 's/StartSpan\(\(\) => /(/g'
run "M14 span end guard removed" $T 's/EndSpan\(client, restore: server \?\? outer\);/client?.Dispose();/g; s/EndSpan\(server, restore: outer\);/server?.Dispose();/g'
run "M15 generic catch counts every fault as a forward failure" $T 's/            if \(answer\.Closed\)\n/            if (false)\n/'
run "M16 the hang-up catch kept" $T 's/        catch \(Exception ex\)\n        \{\n            \/\/ Only a fault the tap did not expect/        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)\n        {\n        }\n        catch (Exception ex)\n        {\n            \/\/ Only a fault the tap did not expect/'
run "M17 no-identity line by subtraction" $T 's/var uncaptured = Interlocked\.Read\(ref _uncaptured\);/var uncaptured = handled - RequestsCaptured;/'
run "M18 span start guard leaves the orphan current" $T 's/            Activity\.Current = before;\n            CaptureFault/            CaptureFault/'
run "M19 span end guard leaves the span current" $T 's/            if \(Activity\.Current == span\)\n                Activity\.Current = restore;\n//'
echo done >> "$OUT"
