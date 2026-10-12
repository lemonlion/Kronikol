"""R1's mutations (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 5.3), each run once in a worktree of its own.

Usage: python mutate.py <worktree> [mutation-id ...]
Each mutation is an exact-text change (every `old` must occur exactly once; a CRLF file takes its `old` and `new` in
CRLF); the file is restored from memory afterwards, so the worktree is never reset. Prints, per mutation, the facts that
failed: a mutation no fact catches says SURVIVED. The runner is r0/mutate.py's.
"""
import re
import subprocess
import sys

BI = 'src/Kronikol.Extensions.Kafka.BuildInterception/KafkaBuildInterceptor.cs'
PRODUCER = 'src/Kronikol.Extensions.Kafka/TrackingKafkaProducer.cs'
TRACKER = 'src/Kronikol.Extensions.Kafka/KafkaTracker.cs'
TRACKING = 'src/Kronikol.Extensions.Kafka/KafkaProduceTracking.cs'
CONTENT = 'src/Kronikol.Extensions.Kafka/KafkaContent.cs'
CREATOR = 'src/Kronikol/PlantUml/PlantUmlCreator.cs'
NOTES_JS = 'src/Kronikol/Reports/collapsible-notes-script.js'
MENU_JS = 'src/Kronikol/Reports/context-menu-script.js'

BI_TESTS = ('tests/Kronikol.Tests.Kafka.BuildInterception', None)
KAFKA_TESTS = ('tests/Kronikol.Tests.Kafka', None)
CORE_TESTS = ('tests/Kronikol.Tests', 'FullyQualifiedName~MessageKeyLineTests|FullyQualifiedName~DiagramContextMenuTests')
E2E_TESTS = ('tests/Kronikol.Tests.EndToEnd', 'FullyQualifiedName~KafkaNoteTests')

# (id, what, tests, [(file, old, new), ...])
MUTATIONS = [
    ('N1', 'the produce reads Activity.Current when its outcome is reported, not at the call (T24)', KAFKA_TESTS, [
        (TRACKING, '    private W3CIds? Ids() => W3CIds.OfLastTraceParent(Message.Headers) ?? _call.Ambient;',
                   '    private W3CIds? Ids() => W3CIds.OfLastTraceParent(Message.Headers) ?? W3CIds.Of(System.Diagnostics.Activity.Current);')]),
    ('N2', 'a traceparent added to a message that carries one (T26)', KAFKA_TESTS, [
        (TRACKING, '        if (addTraceParent && !Has(message.Headers, W3CIds.TraceParentHeader))', '        if (addTraceParent)')]),
    ('N3', 'a traceparent added whatever wrapper sits inside (T54)', KAFKA_TESTS, [
        (PRODUCER, '    private bool AddsTraceParent => _options.PropagateTraceContext && _lastBeforeConfluent;',
                   '    private bool AddsTraceParent => _options.PropagateTraceContext;')]),
    ('N4', 'PropagateTraceContext ignored (T25)', KAFKA_TESTS, [
        (PRODUCER, '    private bool AddsTraceParent => _options.PropagateTraceContext && _lastBeforeConfluent;',
                   '    private bool AddsTraceParent => _lastBeforeConfluent;')]),
    ('N5', 'flags always 00 (T47)', KAFKA_TESTS, [
        (TRACKER, '    internal string ToTraceParent() => $"00-{TraceId}-{SpanId}-{(Recorded ? "01" : "00")}";',
                  '    internal string ToTraceParent() => $"00-{TraceId}-{SpanId}-00";')]),
    ('N6', 'a consume records its ids whatever test it is recorded under (T55)', KAFKA_TESTS, [
        (TRACKER, '        if (call.Test is not { } test || HeaderText(headers, TestTrackingMessageHeaders.TestId) != test.Id)',
                  '        if (call.Test is not { } test)')]),
    ('N7', "a consume falls back to the loop's Activity.Current (T56)", KAFKA_TESTS, [
        (TRACKER, '        return (traced, W3CIds.OfLastTraceParent(headers));', '        return (traced, W3CIds.OfLastTraceParent(headers) ?? call.Ambient);')]),
    ('N8', 'the produce takes a new Kronikol trace id, not the request being served (T57)', KAFKA_TESTS, [
        (TRACKER, '            InboundTraceId() ?? Guid.NewGuid(),', '            Guid.NewGuid(),')]),
    ('N9', "the consume ignores the message's Kronikol trace id (T57)", KAFKA_TESTS, [
        (TRACKER, '            ? call with { TraceId = traceId }', '            ? call')]),
    ('N10', 'upper case lower-cased before parsing, the first draft (T58)', KAFKA_TESTS, [
        (TRACKER, '        return ActivityContext.TryParse(text, null, out var context)', '        return ActivityContext.TryParse(text.ToLowerInvariant(), null, out var context)')]),
    ('N11', 'version 01 refused, the first draft (T58)', KAFKA_TESTS, [
        (TRACKER, '        return ActivityContext.TryParse(text, null, out var context)',
                  '        return text.StartsWith("00-", StringComparison.Ordinal) && ActivityContext.TryParse(text, null, out var context)')]),
    ('N12', 'the listener not started by a Kafka tracker (T31, F14)', KAFKA_TESTS, [
        (TRACKER, '        EnsureListenerStarted();\n        return new KafkaCall(', '        return new KafkaCall(')]),
    ('N13', "the key moved back into the body (T59)", KAFKA_TESTS, [
        (CONTENT, '        return new KafkaPayload(options.LogMessageValue ? Text(message.Value) : null, headers.ToArray());',
                  '        return new KafkaPayload(options.LogMessageValue ? $"Key: {Text(message.Key)}, Value: {Text(message.Value)}" : null, headers.ToArray());')]),
    ('N14', "a consume keeps Kronikol's own headers (T59)", KAFKA_TESTS, [
        (CONTENT, '                if (!leaveOutKronikols || Array.IndexOf(KronikolHeaders, header.Key) < 0)', '                if (true)')]),
    ('N15', 'a header that is not UTF-8 decoded lossily (T59)', KAFKA_TESTS, [
        (CONTENT, '            return StrictUtf8.GetString(bytes);', '            return Encoding.UTF8.GetString(bytes);')]),
    ('N16', 'binary bytes read as text (T59)', KAFKA_TESTS, [
        (CONTENT, '        if (!HttpContentReader.ContainsBinaryContent(bytes))\n            return Encoding.UTF8.GetString(bytes);',
                  '        if (bytes.Length > 0)\n            return Encoding.UTF8.GetString(bytes);')]),
    ('N17', 'a protobuf message serialised as an object, not its JSON (T59)', KAFKA_TESTS, [
        (CONTENT, '        _ => ProtobufJson(value) ?? TrackingSafeSerializer.Serialize(value),', '        _ => TrackingSafeSerializer.Serialize(value),')]),
    ('N18', "numbers written in the current culture (T59)", KAFKA_TESTS, [
        (CONTENT, '            ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),', '            ((IFormattable)value).ToString(null, CultureInfo.CurrentCulture),')]),
    ('N19', "TrackConsume's scope leaves the message's identity current (T61)", KAFKA_TESTS, [
        (TRACKER, '            if (identified)\n                TestIdentityScope.ClearMessageIdentity();\n', '')]),
    ('N20', 'a produce reported twice (T61)', KAFKA_TESTS, [
        (TRACKING, '        if (Interlocked.Exchange(ref _reported, 1) != 0)\n            return;\n', '')]),
    ('N21', 'the Kronikol trace id sent from a produce outside any scenario (Q18)', KAFKA_TESTS, [
        (TRACKING, '            if (!Has(message.Headers, TestTrackingMessageHeaders.TraceId))\n                missing.Add((TestTrackingMessageHeaders.TraceId, call.TraceId.ToString()));\n        }\n',
                   '        }\n        if (!Has(message.Headers, TestTrackingMessageHeaders.TraceId))\n            missing.Add((TestTrackingMessageHeaders.TraceId, call.TraceId.ToString()));\n')]),
    ('N22', 'the first traceparent read, not the last (T54)', KAFKA_TESTS, [
        (TRACKER, '        if (headers is null || !headers.TryGetLastBytes(TraceParentHeader, out var bytes))\n            return null;',
                  '        var bytes = headers?.FirstOrDefault(h => h.Key == TraceParentHeader)?.GetValueBytes();\n        if (bytes is null)\n            return null;')]),
    ('N23', 'a hand-written log records no ids (T30)', KAFKA_TESTS, [
        (TRACKER, '        LogProduce(call, op, content, call.At, ids: call.Ambient);', '        LogProduce(call, op, content, call.At);')]),
    ('N35', 'the scope answers the consume at its start (T41)', KAFKA_TESTS, [
        (TRACKER, '        return new ConsumeScope(identified, entry, span, previous);',
                  '        entry?.Answer(call.At, failureStatus: null, error: null);\n        return new ConsumeScope(identified, entry: null, span, previous);')]),
    ('N36', "the scope's span a child of the span current where the loop polls (T56)", KAFKA_TESTS, [
        (TRACKER, '        var previous = Activity.Current;\n        Activity.Current = null;\n', '        var previous = Activity.Current;\n')]),
    ('N37', 'the span current before the scope not made current again (T62)', KAFKA_TESTS, [
        (TRACKER, '                Activity.Current = previous is { IsStopped: false } ? previous : null;\n', '')]),
    ('N38', "the scope's span a child of the message's traceparent whatever test the consume is under (T55)", KAFKA_TESTS, [
        (TRACKER, '        var (call, messageIds) = ConsumedTrace(Capture(), result.Message?.Headers);\n        Interlocked.Increment(ref _invocationCount);',
                  '        var (call, _) = ConsumedTrace(Capture(), result.Message?.Headers);\n        var messageIds = W3CIds.OfLastTraceParent(result.Message?.Headers);\n        Interlocked.Increment(ref _invocationCount);')]),
    ('N39', 'Kronikol.Kafka not a well-known source (T42)', KAFKA_TESTS, [
        ('src/Kronikol/InternalFlow/InternalFlowSpanCollector.cs', '        "Grpc.Net.Client",\n        "Kronikol.Kafka"\n', '        "Grpc.Net.Client"\n')]),
    ('N40', 'the deferred response takes the phase current when it is answered (T63)', KAFKA_TESTS, [
        (TRACKER, '                AttributionSource = test.Source,\n                Phase = call.Phase,\n                Timestamp = at,',
                  '                AttributionSource = test.Source,\n                Phase = TestPhaseContext.Current,\n                Timestamp = at,')]),
    ('N41', 'a scope disposed twice answers twice', KAFKA_TESTS, [
        (TRACKER, '            if (Interlocked.Exchange(ref _disposed, 1) != 0)\n                return;\n            entry?.Answer(', '            entry?.Answer(')]),
    ('N42', "the scope's span ids not recorded on the consume (T40)", KAFKA_TESTS, [
        (TRACKER, 'incoming: true, W3CIds.Of(span) ?? messageIds);', 'incoming: true, messageIds);')]),
    ('N24', "DependentProducerBuilder's Build() not patched (T22)", BI_TESTS, [
        (BI, '        EnsurePatched(typeof(DependentProducerBuilder<,>), typeof(TKey), typeof(TValue));\n', '')]),
    ('N25', 'the key line drawn in header ink (T60)', CORE_TESTS, [
        (CREATOR, '                return NoteKeyLabel + EscapeCreoleMarkup(', '                return NotePalette.HeaderTag + NoteKeyLabel + EscapeCreoleMarkup(')]),
    ('N26', "a key's line breaks kept", CORE_TESTS, [
        (CREATOR, 'value.ReplaceLineEndings(" "), MaxNoteKeyChars)', 'value, MaxNoteKeyChars)')]),
    ('N27', 'a key drawn without escaping its creole markup', CORE_TESTS, [
        (CREATOR, '                return NoteKeyLabel + EscapeCreoleMarkup(PlantUmlStatementLimits.TruncateLabel(value.ReplaceLineEndings(" "), MaxNoteKeyChars));',
                  '                return NoteKeyLabel + PlantUmlStatementLimits.TruncateLabel(value.ReplaceLineEndings(" "), MaxNoteKeyChars);')]),
    ('N28', 'a long key not cut', CORE_TESTS, [
        (CREATOR, '    private const int MaxNoteKeyChars = 120;', '    private const int MaxNoteKeyChars = 100_000;')]),
    ('N29', 'the key line dropped with every header excluded', CORE_TESTS, [
        (CREATOR, 'FormatNoteContent(excludeAllHeaders ? KeyHeaderOnly(effectiveHeaders) : effectiveHeaders, content, excludedHeaders, RequestResponseType.Request,',
                  'FormatNoteContent(excludeAllHeaders ? Array.Empty<(string Key, string? Value)>() : effectiveHeaders, content, excludedHeaders, RequestResponseType.Request,')]),
    ('N30', 'the key drawn among the headers too', CORE_TESTS, [
        (CREATOR, '            .Where(y => y.Key != MessageKeyHeader && !excludedHeaders.Contains(y.Key))', '            .Where(y => !excludedHeaders.Contains(y.Key))')]),
    ('N31', "one script's key-line label differs from the generator's", CORE_TESTS, [
        (MENU_JS, '    var NOTE_KEY_LINE = /^<b>Key<\\/b> /;', '    var NOTE_KEY_LINE = /^<b>Key:<\\/b> /;')]),
    ('N32', 'the key line hidden with the headers (T60)', E2E_TESTS, [
        (NOTES_JS, '            if (inNote && hideHeaders && NOTE_HEADER_TAG.test(trimmed)) { justSkippedGray = true; continue; }',
                   '            if (inNote && hideHeaders && isNoteMetaLine(trimmed)) { justSkippedGray = true; continue; }')]),
    ('N33', "the key line read as the payload's first line by the YAML view (T60)", E2E_TESTS, [
        (NOTES_JS, '            if (isNoteMetaLine(trimmed)) { afterGray = true; continue; }', '            if (NOTE_HEADER_TAG.test(trimmed)) { afterGray = true; continue; }')]),
    ('N34', "the collapsed note's preview shows the key line", E2E_TESTS, [
        (NOTES_JS, "            .filter(function(l) { return !isNoteMetaLine(l); });", "            .filter(function(l) { return !NOTE_HEADER_TAG.test(l); });")]),
]


def stop_leftovers(worktree):
    """A mutation can leave the xUnit v3 test process running after dotnet test returns; stop whatever still runs from
    the worktree's bin directories, so the next mutation's build can copy (r0/mutate.py's note)."""
    script = ("Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath -like '"
              + worktree.replace('/', '\\') + "\\*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -Confirm:$false }")
    subprocess.run(['pwsh', '-NoProfile', '-Command', script], capture_output=True)


def run(worktree, ids):
    results = []
    for mid, what, (project, test_filter), edits in MUTATIONS:
        if ids and mid not in ids:
            continue
        originals = {}
        try:
            for path, old, new in edits:
                full = f'{worktree}/{path}'
                if full not in originals:
                    with open(full, encoding='utf-8', newline='') as f:
                        originals[full] = f.read()
                with open(full, encoding='utf-8', newline='') as f:
                    text = f.read()
                if '\r\n' in text:
                    old, new = old.replace('\n', '\r\n'), new.replace('\n', '\r\n')
                assert text.count(old) == 1, f'{mid}: {path} holds the text {text.count(old)} times'
                with open(full, 'w', encoding='utf-8', newline='') as f:
                    f.write(text.replace(old, new))
            command = ['dotnet', 'test', project, '-f', 'net10.0', '-nologo', '--logger', 'console;verbosity=normal']
            if test_filter:
                command += ['--filter', test_filter]
            try:
                output = subprocess.run(command, cwd=worktree, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=900).stdout
            except subprocess.TimeoutExpired as expired:
                partial = expired.stdout or ''
                output = (partial.decode('utf-8', 'replace') if isinstance(partial, bytes) else partial) + '\nHUNG'
            failed = sorted(set(re.findall(r'^\s+Failed (\S+)', output, re.MULTILINE)))
            build_error = re.search(r'error (?:CS|MSB)\d+[^\n]*', output)
            aborted = 'HUNG' in output or 'Test Run Aborted' in output or 'test run was aborted' in output.lower()
            if build_error and not failed:
                verdict = f'DID NOT BUILD: {build_error.group(0)[:200]}'
            elif aborted:
                verdict = 'NO VERDICT, the run hung or its test process ended; failed before that: ' + ', '.join(name.split('.')[-1] for name in failed)
            elif failed:
                verdict = f'killed by {len(failed)}: ' + ', '.join(name.split('.')[-1] for name in failed)
            else:
                verdict = 'SURVIVED'
        finally:
            for full, text in originals.items():
                with open(full, 'w', encoding='utf-8', newline='') as f:
                    f.write(text)
            stop_leftovers(worktree)
        line = f'{mid} ({what}): {verdict}'
        print(line, flush=True)
        results.append(line)
    return results


if __name__ == '__main__':
    run(sys.argv[1], set(sys.argv[2:]))
