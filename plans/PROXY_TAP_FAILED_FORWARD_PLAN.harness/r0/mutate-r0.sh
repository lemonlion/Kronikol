#!/bin/bash
# R0 mutations: each must make at least one new fact fail. Usage: mutate-r0.sh <worktree> <outfile>
W=$1; OUT=$2; cd "$W" || exit 1
: > "$OUT"
run() { # name file perl-expr project filter
  local name=$1 file=$2 expr=$3 proj=$4 filter=$5
  cp "$file" /tmp/mut.bak
  perl -0pi -e "$expr" "$file"
  if cmp -s "$file" /tmp/mut.bak; then echo "$name: SUBSTITUTION DID NOT APPLY" >> "$OUT"; return; fi
  local res; res=$(timeout 600 dotnet test "$proj" --filter "FullyQualifiedName~$filter" 2>&1 | grep -E "Failed!|Passed!|error CS" | head -3)
  cp /tmp/mut.bak "$file"
  if echo "$res" | grep -q "Failed!"; then echo "$name: KILLED ($(echo "$res" | grep -oE 'Failed: +[0-9]+, Passed: +[0-9]+'))" >> "$OUT"
  else echo "$name: SURVIVED ($res)" >> "$OUT"; fi
}
P=src/Kronikol.Extensions.ProxyTap; O=src/Kronikol.Extensions.Otlp
run "M1 ProxyTap forward by relative resolution" $P/ProxyTap.cs 's/new HttpRequestMessage\(new HttpMethod\(request\.HttpMethod\), forwardUri\)/new HttpRequestMessage(new HttpMethod(request.HttpMethod), pathAndQuery)/' tests/Kronikol.Tests.ProxyTap ProxyTapForwardTargetTests
run "M2 ProxyTap record by relative resolution" $P/ProxyTap.cs 's/request\.HttpMethod, forwardUri,/request.HttpMethod, new Uri(_options.ForwardBaseUri!, pathAndQuery),/' tests/Kronikol.Tests.ProxyTap ProxyTapForwardTargetTests
run "M3 ProxyTap Validate without the scheme test" $P/ProxyTapOptions.cs 's/ \|\| \(ForwardBaseUri\.Scheme != Uri\.UriSchemeHttp && ForwardBaseUri\.Scheme != Uri\.UriSchemeHttps\)//' tests/Kronikol.Tests.ProxyTap ProxyTapForwardTargetTests
run "M4 OtlpTap Validate without the scheme test" $O/OtlpTapOptions.cs 's/!ForwardBaseUri\.IsAbsoluteUri \|\| \(ForwardBaseUri\.Scheme != Uri\.UriSchemeHttp && ForwardBaseUri\.Scheme != Uri\.UriSchemeHttps\)/!ForwardBaseUri.IsAbsoluteUri/' tests/Kronikol.Tests.Otlp OtlpTapForwardTargetTests
run "M6 OtlpTap absolute target of any scheme" $O/OtlpTap.cs 's/out var absolute\) && \(absolute\.Scheme == Uri\.UriSchemeHttp \|\| absolute\.Scheme == Uri\.UriSchemeHttps\)/out var absolute)/' tests/Kronikol.Tests.Otlp OtlpTapForwardTargetTests
run "M7 OtlpTap forwards a target with no path as written" $O/OtlpTap.cs 's/if \(OriginForm\(request\.Target\) is not \{ \} origin\)/var origin = OriginForm(request.Target) ?? "\/" + request.Target;\n        if (origin is null)/' tests/Kronikol.Tests.Otlp OtlpTapForwardTargetTests
run "M5 OtlpTap path test after the absolute test" $O/OtlpTap.cs 's/        if \(target\.StartsWith\(\x27\/\x27\)\)\n            return target;\n        return Uri\.TryCreate\(target, UriKind\.Absolute, out var absolute\) && \(absolute\.Scheme == Uri\.UriSchemeHttp \|\| absolute\.Scheme == Uri\.UriSchemeHttps\)\n            \? absolute\.PathAndQuery\n            : null;/        if (Uri.TryCreate(target, UriKind.Absolute, out var absolute))\n            return absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps ? absolute.PathAndQuery : null;\n        return target.StartsWith(\x27\/\x27) ? target : null;/' tests/Kronikol.Tests.Otlp OtlpTapForwardTargetTests
echo done >> "$OUT"
