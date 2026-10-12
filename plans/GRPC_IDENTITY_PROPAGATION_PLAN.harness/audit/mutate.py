# Mutation check of the #134 audit fixes: each mutation must turn at least one named fact red.
# Usage: python mutate.py <snapshot worktree> <output file>
import io, os, re, subprocess, sys, time

wt, out = sys.argv[1], sys.argv[2]
os.chdir(wt)

GRPC = 'tests/Kronikol.Tests.Grpc/Kronikol.Tests.Grpc.csproj'
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
SPANNER = 'tests/Kronikol.Tests.Spanner/Kronikol.Tests.Spanner.csproj'
INT = 'src/Kronikol.Extensions.Grpc/GrpcTrackingInterceptor.cs'
STORE = 'src/Kronikol/InternalFlow/InternalFlowSpanStore.cs'
HANDLER = 'src/Kronikol/Tracking/TestTrackingMessageHandler.cs'
PROXY = 'src/Kronikol/Tracking/TrackingProxy.cs'
DIAG = 'src/Kronikol/Reports/DiagnosticReportGenerator.cs'
SPAN_FMT = 'src/Kronikol.Extensions.Spanner/SpannerResponseFormatter.cs'

FINALLY_RESTORE = "        finally\n        {\n            Activity.Current = callerActivity;\n        }"
WITH_META = "context = WithCallMetadata(context, plan, activityTraceId, activitySpanId, recorded);"
STREAM_CATCH = ("            if (started is not null)\n                started.Failed(ex);\n            else\n"
                "                InternalFlowSpanStore.Complete(activity);")

# (id, what, file, old, new, occurrence or None for "exactly one", project, filter)
M = [
    ('M1', 'the span store stops restoring the stopping flow\'s current span', STORE,
     "            if (!ReferenceEquals(current, activity))\n                Activity.Current = current;\n", "", None,
     GRPC, 'FullyQualifiedName~Disposing_a_stream'),
    ('M2', 'Dispose catches only InvalidOperationException from GetStatus', INT,
     "            catch\n            {\n                // The call has not finished (InvalidOperationException), or it cannot say: disposing it cancels it.\n            }",
     "            catch (InvalidOperationException)\n            {\n            }", None,
     GRPC, 'FullyQualifiedName~A_stream_whose_status_cannot_be_read'),
    ('M3', 'Dispose records only when the call\'s own dispose returns', INT,
     "            try\n            {\n                dispose();\n            }\n            finally\n            {",
     "            dispose();\n            {", None,
     GRPC, 'FullyQualifiedName~A_stream_whose_dispose_throws'),
    ('M4', 'the async unary response catches only RpcException', INT,
     "        catch (Exception ex)\n        {\n            var (status, content) = Failure(ex);\n            LogResponse(testInfo, label, content, uri, serviceName,\n                traceId, requestResponseId, status, activityTraceId, activitySpanId, opInfo);\n            throw;\n        }\n        finally",
     "        catch (RpcException ex)\n        {\n            var (status, content) = Failure(ex);\n            LogResponse(testInfo, label, content, uri, serviceName,\n                traceId, requestResponseId, status, activityTraceId, activitySpanId, opInfo);\n            throw;\n        }\n        finally", None,
     GRPC, 'FullyQualifiedName~A_unary_call_that_fails_without_an_RpcException'),
    ('M5', 'the blocking unary call catches only RpcException', INT,
     "        catch (Exception ex)\n        {\n            var (status, content) = Failure(ex);\n            LogResponse(testInfo, label, content, uri, serviceName,\n                traceId, requestResponseId, status, activityTraceId, activitySpanId, opInfo);\n            throw;\n        }\n    }\n\n    public override AsyncServerStreamingCall",
     "        catch (RpcException ex)\n        {\n            var (status, content) = Failure(ex);\n            LogResponse(testInfo, label, content, uri, serviceName,\n                traceId, requestResponseId, status, activityTraceId, activitySpanId, opInfo);\n            throw;\n        }\n    }\n\n    public override AsyncServerStreamingCall", None,
     GRPC, 'FullyQualifiedName~A_unary_call_that_fails_without_an_RpcException'),
    ('M6', 'an OperationCanceledException is recorded as Unknown', INT,
     "            OperationCanceledException => (StatusCode.Cancelled, exception.Message),\n", "", None,
     GRPC, 'FullyQualifiedName~A_unary_call_that_fails_without_an_RpcException|FullyQualifiedName~A_stream_whose_read_fails'),
    ('M7', 'an async unary continuation that throws records nothing', INT,
     "            if (requestLogged)\n            {", "            if (requestLogged && false)\n            {", None,
     GRPC, 'FullyQualifiedName~A_call_whose_continuation_throws'),
    ('M8a', 'a server-streaming continuation that throws records nothing', INT, STREAM_CATCH,
     "                InternalFlowSpanStore.Complete(activity);", 0, GRPC, 'FullyQualifiedName~A_call_whose_continuation_throws'),
    ('M8b', 'a client-streaming continuation that throws records nothing', INT, STREAM_CATCH,
     "                InternalFlowSpanStore.Complete(activity);", 1, GRPC, 'FullyQualifiedName~A_call_whose_continuation_throws'),
    ('M8c', 'a duplex continuation that throws records nothing', INT, STREAM_CATCH,
     "                InternalFlowSpanStore.Complete(activity);", 2, GRPC, 'FullyQualifiedName~A_call_whose_continuation_throws'),
    ('M9', 'a trace id in the caller\'s metadata is not the call\'s own', INT,
     "            CallerTraceId(context) ?? InboundTraceId() ?? Guid.NewGuid(),", "            InboundTraceId() ?? Guid.NewGuid(),", None,
     GRPC, 'FullyQualifiedName~A_trace_id_the_caller_put_in_the_metadata'),
    ('M10', 'the ignore header is compared case-sensitively', HANDLER,
     "string.Equals(x.Key, TestTrackingHttpHeaders.Ignore, StringComparison.OrdinalIgnoreCase)", "x.Key == TestTrackingHttpHeaders.Ignore", None,
     CORE, 'FullyQualifiedName~The_ignore_header_is_honoured'),
    ('M11', 'the trace id the request carries is not the call\'s own', HANDLER,
     "        var traceId = TraceIdOf(request.Headers)\n                      ?? (", "        var traceId = (", None,
     CORE, 'FullyQualifiedName~A_trace_id_the_caller_set_on_the_request|FullyQualifiedName~A_request_sent_again_is_logged'),
    ('M12', 'HeadersToForward adds a header the request already carries', HANDLER,
     "            if (!request.Headers.Contains(header) && contextHeaders.TryGetValue(header, out var value))",
     "            if (contextHeaders.TryGetValue(header, out var value))", None,
     CORE, 'FullyQualifiedName~A_forwarded_header_is_not_added_again|FullyQualifiedName~A_header_the_caller_set_is_left'),
    ('M13', 'the proxy leaves its span current after an async target', PROXY,
     "                if (activity is not null)\n                    Activity.Current = callerActivity;\n", "", None,
     CORE, 'FullyQualifiedName~TrackingProxyTests'),
    ('M14a', 'a server stream leaves its span current once it starts', INT, FINALLY_RESTORE, "        finally\n        {\n        }", 1,
     GRPC, 'FullyQualifiedName~The_caller_has_its_own_current_span_back'),
    ('M14b', 'a client stream leaves its span current once it starts', INT, FINALLY_RESTORE, "        finally\n        {\n        }", 2,
     GRPC, 'FullyQualifiedName~The_caller_has_its_own_current_span_back'),
    ('M14c', 'a duplex stream leaves its span current once it starts', INT, FINALLY_RESTORE, "        finally\n        {\n        }", 3,
     GRPC, 'FullyQualifiedName~The_caller_has_its_own_current_span_back'),
]
for k, kind in enumerate(['async unary', 'blocking unary', 'server streaming', 'client streaming', 'duplex']):
    M.append((f'M15{"abcde"[k]}', f'the {kind} call\'s flags ignore a recorded span', INT, WITH_META,
              "context = WithCallMetadata(context, plan, activityTraceId, activitySpanId, false);", k,
              GRPC, 'FullyQualifiedName~The_flags_say_sampled_when_another_listener_records_the_span'))
M += [
    ('M16', 'a stream\'s response takes the phase current when it ends', INT,
     "                activityTraceId, activitySpanId, opInfo, phase);", "                activityTraceId, activitySpanId, opInfo, null);", None,
     GRPC, 'FullyQualifiedName~A_streams_response_is_logged_in_the_phase'),
    ('M17', 'binary entries of the caller\'s metadata are dropped', INT,
     "            foreach (var entry in callerHeaders)\n                headers.Add(entry);",
     "            foreach (var entry in callerHeaders)\n                if (!entry.IsBinary) headers.Add(entry);", None,
     GRPC, 'FullyQualifiedName~A_binary_entry_of_the_callers'),
    ('M18', 'the caller name is sent raw', INT,
     "TrackingHeaderValue.Encode(_options.CallerName)", "_options.CallerName", None,
     GRPC, 'FullyQualifiedName~A_caller_name_that_is_not_plain_ASCII'),
    ('M19', 'PropagateTestIdentity is ignored', INT,
     "            _options.PropagateTestIdentity && identity.Value.IsAttributed);", "            identity.Value.IsAttributed);", None,
     GRPC, 'FullyQualifiedName~MultiHostBackgroundTests|FullyQualifiedName~With_PropagateTestIdentity_off'),
    ('M20', 'the accessor cell warns only when every instance was invoked', DIAG,
     "        return instances.Any(c => c.WasInvoked)", "        return instances.All(c => c.WasInvoked)", None,
     CORE, 'FullyQualifiedName~A_group_with_no_accessor_warns'),
    ('M21', 'a stream disposed after it failed is recorded Cancelled', INT,
     "                else\n                    Failed(new RpcException(known));",
     "                else\n                    Record(\"Cancelled: disposed\", MapGrpcStatusToHttp(StatusCode.Cancelled));", None,
     GRPC, 'FullyQualifiedName~A_stream_disposed_once_it_has_failed'),
    ('M22', 'a stream\'s read records only an RpcException', INT,
     "            catch (Exception ex)\n            {\n                outcome.Failed(ex);\n                throw;\n            }",
     "            catch (RpcException ex)\n            {\n                outcome.Failed(ex);\n                throw;\n            }", None,
     GRPC, 'FullyQualifiedName~A_stream_whose_read_fails'),
    ('M23', 'gRPC messages are written with JsonFormatter.Default', INT,
     "                return JsonFormatter.ToDiagnosticString(protoMsg);", "                return JsonFormatter.Default.Format(protoMsg);", None,
     GRPC, 'FullyQualifiedName~MessageContentTests'),
    ('M24', 'a message that cannot be written fails the call', INT,
     "        catch (Exception ex)\n        {\n            return $\"({message?.GetType().Name} could not be written: {ex.Message})\";\n        }",
     "        finally\n        {\n        }", None,
     GRPC, 'FullyQualifiedName~MessageContentTests'),
    ('M25', 'Spanner\'s raw format uses JsonFormatter.Default', SPAN_FMT,
     "            return JsonFormatter.ToDiagnosticString(protoMsg);", "            return JsonFormatter.Default.Format(protoMsg);", None,
     SPANNER, 'FullyQualifiedName~FormatRaw'),
]

def read(p): return io.open(p, encoding='utf-8', newline='').read()
def write(p, s): io.open(p, 'w', encoding='utf-8', newline='').write(s)

def mutate(s, old, new, occ):
    n = s.count(old)
    if occ is None:
        assert n == 1, ('anchor count', n)
        return s.replace(old, new)
    idx = -1
    for _ in range(occ + 1):
        idx = s.index(old, idx + 1)
    return s[:idx] + new + s[idx + len(old):]

def run(cmd):
    p = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
    return p.returncode, p.stdout + p.stderr

with io.open(out, 'w', encoding='utf-8', newline='\n') as log:
    log.write('Mutations of the #134 audit fixes, run on the snapshot ' + subprocess.run(['git', 'rev-parse', '--short', 'HEAD'], capture_output=True, text=True).stdout.strip() + '\n\n')
    killed = 0
    for mid, what, path, old, new, occ, proj, flt in M:
        original = read(path)
        t0 = time.time()
        try:
            write(path, mutate(original, old, new, occ))
            rc, b = run(['dotnet', 'build', proj, '-c', 'Debug', '-v', 'q', '-nologo'])
            if rc != 0:
                errs = [l for l in b.splitlines() if ' error ' in l][:2]
                verdict, detail = 'BUILD FAILED', ' | '.join(errs)
            else:
                rc, t = run(['dotnet', 'test', proj, '-c', 'Debug', '--no-build', '--filter', flt])
                fails = sorted(set(re.sub(r'^\s*\[xUnit\.net [^\]]*\]\s*', '', l).replace(' [FAIL]', '').strip()
                                   for l in t.splitlines() if '[FAIL]' in l))
                summary = [l.strip() for l in t.splitlines() if re.search(r'(Passed|Failed)!  -', l)]
                if fails:
                    verdict, detail = 'KILLED', f'{len(fails)} red, e.g. ' + fails[0].split('.')[-1][:120]
                    killed += 1
                else:
                    verdict, detail = 'SURVIVED', ' '.join(summary) or t[-300:]
        finally:
            write(path, original)
        log.write(f'{mid} {verdict}: {what} ({time.time() - t0:.0f} s)\n    {detail}\n')
        log.flush()
    log.write(f'\n{killed} of {len(M)} killed\n')
    # Leave the snapshot built as it was.
    for proj in (GRPC, CORE, SPANNER):
        run(['dotnet', 'build', proj, '-c', 'Debug', '-v', 'q', '-nologo'])
print('done')
