"""R2's mutations (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 5.3), each run once in a worktree of its own.

Usage: python mutate.py <worktree> [mutation-id ...]
Each mutation is an exact-text change (every `old` must occur exactly once; a CRLF file takes its `old` and `new` in
CRLF); the file is restored from memory afterwards, so the worktree is never reset. Prints, per mutation, the facts that
failed: a mutation no fact catches says SURVIVED. The runner is r0/mutate.py's.
"""
import re
import subprocess
import sys

PRODUCER = 'src/Kronikol.Extensions.Kafka/TrackingKafkaProducer.cs'
CONSUMER = 'src/Kronikol.Extensions.Kafka/TrackingKafkaConsumer.cs'
TRACKER = 'src/Kronikol.Extensions.Kafka/KafkaTracker.cs'
TRACKING = 'src/Kronikol.Extensions.Kafka/KafkaProduceTracking.cs'

KAFKA_TESTS = ('tests/Kronikol.Tests.Kafka', None)

PUBLISH_START = ('        var publish = publishSpan && options.TraceMessageProcessing\n'
                 '            ? Source.StartActivity($"{topic} publish", ActivityKind.Producer)\n'
                 '            : null;\n')


def next_call(signature):
    """The EndProcessing() a consumer method opens with, by its signature line."""
    return (CONSUMER, f'    {signature}\n    {{\n        EndProcessing();\n', f'    {signature}\n    {{\n')


# (id, what, tests, [(file, old, new), ...])
MUTATIONS = [
    ('M1', 'no publish span (T64)', KAFKA_TESTS, [
        (TRACKER, PUBLISH_START, '        Activity? publish = null;\n')]),
    ('M2', "the produce captured before its publish span starts, so the traceparent names the caller's span (T64)", KAFKA_TESTS, [
        (TRACKER, '        var previous = Activity.Current;\n' + PUBLISH_START,
                  '        var previous = Activity.Current;\n        var captured = Capture();\n' + PUBLISH_START),
        (TRACKER, '        return new(this, Capture(), topic, operation, message, options, addTraceParent, publish, previous);',
                  '        return new(this, captured, topic, operation, message, options, addTraceParent, publish, previous);')]),
    ('M3', "a hand-written decorator's TrackProduce starts a publish span it never ends", KAFKA_TESTS, [
        (TRACKER, '        var publish = publishSpan && options.TraceMessageProcessing\n',
                  '        var publish = options.TraceMessageProcessing\n')]),
    ('M4', 'a publish span with TraceMessageProcessing off', KAFKA_TESTS, [
        (TRACKER, '        var publish = publishSpan && options.TraceMessageProcessing\n', '        var publish = publishSpan\n')]),
    ('M5', 'the publish span never ended', KAFKA_TESTS, [
        (TRACKING, '        _publish.Stop();\n', '')]),
    ('M6', "the span current before the produce made current again even when the caller's own span is current", KAFKA_TESTS, [
        (TRACKING, '        var restored = current != _publish ? current : _previous;', '        var restored = _previous;')]),
    ('M7', 'an ended span left current after a produce', KAFKA_TESTS, [
        (TRACKING, '        Activity.Current = restored is { IsStopped: false } ? restored : null;\n    }',
                   '        Activity.Current = restored;\n    }')]),
    ('M8', 'ProduceAsync never ends its publish span', KAFKA_TESTS, [
        (PRODUCER, '        finally\n        {\n            tracking.EndPublish();\n        }\n\n        tracking.Delivered(result);',
                   '        finally\n        {\n        }\n\n        tracking.Delivered(result);')]),
    ('M9', 'Produce never ends its publish span', KAFKA_TESTS, [
        (PRODUCER, '        finally\n        {\n            tracking.EndPublish();\n        }\n\n        void Send(',
                   '        finally\n        {\n        }\n\n        void Send(')]),
    ('M10', 'Consume answers at once under TraceMessageProcessing, no scope (T62)', KAFKA_TESTS, [
        (CONSUMER, '        if (_options.TraceMessageProcessing)\n            _processing = _tracker.BeginConsume(result, _options);\n'
                   '        else\n            _tracker.Consumed(result, _options);',
                   '        _tracker.Consumed(result, _options);')]),
    ('M11', 'the next Consume does not close the last scope', KAFKA_TESTS, [
        (CONSUMER, '        // The last message was handled once the loop polls again: its consume is answered before this poll waits.\n'
                   '        EndProcessing();\n',
                   '        // The last message was handled once the loop polls again: its consume is answered before this poll waits.\n')]),
    ('M12', 'Commit() does not close the scope', KAFKA_TESTS, [next_call('public List<TopicPartitionOffset> Commit()')]),
    ('M13', 'Commit(offsets) does not close the scope', KAFKA_TESTS, [next_call('public void Commit(IEnumerable<TopicPartitionOffset> offsets)')]),
    ('M14', 'Commit(result) does not close the scope', KAFKA_TESTS, [next_call('public void Commit(ConsumeResult<TKey, TValue> result)')]),
    ('M15', 'StoreOffset(result) does not close the scope', KAFKA_TESTS, [next_call('public void StoreOffset(ConsumeResult<TKey, TValue> result)')]),
    ('M16', 'StoreOffset(offset) does not close the scope', KAFKA_TESTS, [next_call('public void StoreOffset(TopicPartitionOffset offset)')]),
    ('M17', 'Close does not close the scope', KAFKA_TESTS, [next_call('public void Close()')]),
    ('M18', 'Dispose does not close the scope', KAFKA_TESTS, [next_call('public void Dispose()')]),
    ('M19', "the scope closed at the next call as a using block closes it: the span before the scope made current, not the call's own (T62)", KAFKA_TESTS, [
        (CONSUMER, 'Interlocked.Exchange(ref _processing, null)?.CloseAtNextCall();', 'Interlocked.Exchange(ref _processing, null)?.Dispose();')]),
    ('M20', "at the next call the scope's own ended span left in place of the span before it", KAFKA_TESTS, [
        (TRACKER, '                var restored = atNextCall && current != span ? current : previous;',
                  '                var restored = atNextCall ? current : previous;')]),
    ('M21', 'an ended span left current when a scope closes (T62)', KAFKA_TESTS, [
        (TRACKER, '                Activity.Current = restored is { IsStopped: false } ? restored : null;',
                  '                Activity.Current = restored;')]),
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
