"""R0's mutations (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 5.3), each run once in a worktree of its own.

Usage: python mutate.py <worktree> [mutation-id ...]
Each mutation is an exact-text change (every `old` must occur exactly once); the file is restored from memory afterwards,
so the worktree is never reset. Prints, per mutation, the facts that failed: a mutation no fact catches says SURVIVED.
"""
import re
import subprocess
import sys

BI = 'src/Kronikol.Extensions.Kafka.BuildInterception/KafkaBuildInterceptor.cs'
TIER = 'src/Kronikol.Extensions.Kafka.BuildInterception/TieredCompilationCheck.cs'
PRODUCER = 'src/Kronikol.Extensions.Kafka/TrackingKafkaProducer.cs'
CONSUMER = 'src/Kronikol.Extensions.Kafka/TrackingKafkaConsumer.cs'
TRACKER = 'src/Kronikol.Extensions.Kafka/KafkaTracker.cs'
INTERCEPTOR = 'src/Kronikol.Extensions.Kafka/KafkaTrackingInterceptor.cs'
CLASSIFIER = 'src/Kronikol.Extensions.Kafka/KafkaOperationClassifier.cs'
GENERATOR = 'src/Kronikol/Reports/ReportGenerator.cs'
PROCESS = 'src/Kronikol/Reports/ProcessDiagnostics.cs'

BI_TESTS = ('tests/Kronikol.Tests.Kafka.BuildInterception', None)
KAFKA_TESTS = ('tests/Kronikol.Tests.Kafka', None)
CORE_TESTS = ('tests/Kronikol.Tests', 'FullyQualifiedName~ProcessDiagnosticsReportTests')

# (id, what, tests, [(file, old, new), ...])
MUTATIONS = [
    ('M1', 'the prefix returns true, so the closed-over body runs', BI_TESTS, [
        (BI, '        __result = Builds.GetOrAdd(__instance.GetType(), CompileBuild)(__instance);\n        return false;',
             '        __result = Builds.GetOrAdd(__instance.GetType(), CompileBuild)(__instance);\n        return true;')]),
    ('M2', 'Reset() unpatches again', BI_TESTS, [
        (BI, '    public static void Reset()\n    {\n        KafkaTrackingInterceptor.Reset();\n    }',
             '    public static void Reset()\n    {\n        KafkaTrackingInterceptor.Reset();\n        HarmonyInstance.UnpatchAll(HarmonyId);\n        lock (PatchLock) PatchedShapes.Clear();\n    }')]),
    ('M3', 'the prefix builds by the first builder type of the shape, not the runtime type', BI_TESTS, [
        (BI, '        __result = Builds.GetOrAdd(__instance.GetType(), CompileBuild)(__instance);',
             '        var runtime = __instance.GetType();\n        var key = runtime.IsGenericType ? runtime.GetGenericTypeDefinition().MakeGenericType(runtime.GetGenericArguments().Select(Shape).ToArray()) : runtime;\n        __result = Builds.GetOrAdd(key, _ => CompileBuild(runtime))(__instance);')]),
    ('M4', 'patches keyed by the closed pair instead of the code shape', BI_TESTS, [
        (BI, '        var representative = builderDefinition.MakeGenericType(Shape(key), Shape(value));',
             '        var representative = builderDefinition.MakeGenericType(key, value);')]),
    ('M5', 'the constructor invoked through a call that wraps its exceptions', BI_TESTS, [
        (BI, '        var builder = Expression.Parameter(typeof(object), "builder");\n        var body = Expression.Call(wrap, Expression.New(constructor, Expression.Convert(builder, closedBuilder)));\n        return Expression.Lambda<Func<object, object>>(body, builder).Compile();',
             '        return b => wrap.Invoke(null, [constructor.Invoke([b])])!;')]),
    ('M16', 'the shape computed without recursing into generic value types', BI_TESTS, [
        (BI, '        : type.IsGenericType ? type.GetGenericTypeDefinition().MakeGenericType(type.GetGenericArguments().Select(Shape).ToArray())\n',
             '')]),
    ('M17', 'a shape marked patched before its patch is installed', BI_TESTS, [
        (BI, '        lock (PatchLock)\n        {\n            if (PatchedShapes.Contains(representative))\n                return;\n\n            HarmonyInstance.Patch(representative.GetMethod("Build", Type.EmptyTypes)!, prefix: new HarmonyMethod(BuildPrefixMethod));\n            PatchedShapes.Add(representative);\n        }',
             '        lock (PatchLock)\n        {\n            if (!PatchedShapes.Add(representative))\n                return;\n        }\n        HarmonyInstance.Patch(representative.GetMethod("Build", Type.EmptyTypes)!, prefix: new HarmonyMethod(BuildPrefixMethod));')]),
    ('M18a', 'the tiering check reads the runtimeconfig before the environment', BI_TESTS, [
        (TIER, 'EnvironmentValue("DOTNET_" + setting) ?? EnvironmentValue("COMPlus_" + setting) ?? RuntimeConfigValue(runtimeConfigName) ?? true;',
               'RuntimeConfigValue(runtimeConfigName) ?? EnvironmentValue("DOTNET_" + setting) ?? EnvironmentValue("COMPlus_" + setting) ?? true;')]),
    ('M18b', 'the tiering check takes "false" for off', BI_TESTS, [
        (TIER, '        if (value is null)\n            return null;\n',
               '        if (value is null)\n            return null;\n        if (value == "false")\n            return false;\n')]),
    ('M18c', 'enabling never runs the tiering check', BI_TESTS, [
        (BI, '        EnsurePatched(typeof(ProducerBuilder<,>), typeof(TKey), typeof(TValue));\n        TieringCheck.ReportOnce();',
             '        EnsurePatched(typeof(ProducerBuilder<,>), typeof(TKey), typeof(TValue));')]),
    ('M21', 'trackers registered when enabled rather than at their first client', BI_TESTS, [
        (INTERCEPTOR, '        TrackingState NewState() => new(new KafkaTracker(options, httpContextAccessor, role), options, httpContextAccessor);',
                      '        TrackingState NewState() { var tracker = new KafkaTracker(options, httpContextAccessor, role); tracker.EnsureRegistered(); return new(tracker, options, httpContextAccessor); }')]),
    ('M6', 'identity resolved in the delivery handler, not at the call', KAFKA_TESTS, [
        (PRODUCER, '                _tracker.LogProduce(call, new KafkaOperationInfo(KafkaOperation.Produce, topic, Known(report.Partition), Known(report.Offset)),',
                   '                _tracker.LogProduce(_tracker.Capture() with { At = call.At, Phase = call.Phase }, new KafkaOperationInfo(KafkaOperation.Produce, topic, Known(report.Partition), Known(report.Offset)),')]),
    ('M7', 'phase and verbosity read in the delivery handler', KAFKA_TESTS, [
        (PRODUCER, '                _tracker.LogProduce(call, new KafkaOperationInfo(KafkaOperation.Produce, topic, Known(report.Partition), Known(report.Offset)),',
                   '                _tracker.LogProduce(call with { Phase = TestPhaseContext.Current }, new KafkaOperationInfo(KafkaOperation.Produce, topic, Known(report.Partition), Known(report.Offset)),')]),
    ('M8', 'the request stamped when the outcome is known', KAFKA_TESTS, [
        (TRACKER, '            Phase = call.Phase,\n            Timestamp = call.At,\n', '            Phase = call.Phase,\n            Timestamp = outcomeAt,\n')]),
    ('M9', 'report.Error ignored', KAFKA_TESTS, [
        (PRODUCER, '                var failed = report.Error is { IsError: true };', '                var failed = false;')]),
    ('M10', "the caller's message written to", KAFKA_TESTS, [
        (PRODUCER, '        var headers = new Headers();\n        if (message.Headers is not null)\n            foreach (var header in message.Headers)\n                headers.Add(header.Key, header.GetValueBytes());\n        foreach (var (key, value) in missing)\n            headers.Add(key, Encoding.UTF8.GetBytes(value));\n        return new Message<TKey, TValue> { Key = message.Key, Value = message.Value, Timestamp = message.Timestamp, Headers = headers };',
                   '        message.Headers ??= new Headers();\n        foreach (var (key, value) in missing)\n            message.Headers.Add(key, Encoding.UTF8.GetBytes(value));\n        return message;')]),
    ('M11', 'the background identity stamped', KAFKA_TESTS, [
        (PRODUCER, 'call.Test is not { IsAttributed: true } identity', 'call.Test is not { } identity')]),
    ('M12a', 'a sync Produce that throws left unlogged', KAFKA_TESTS, [
        (PRODUCER, '        catch (Exception exception)\n        {\n            LogFailed(exception);\n            throw;\n        }\n',
                   '        catch (Exception)\n        {\n            throw;\n        }\n'),
        (PRODUCER, '            catch (Exception exception)\n            {\n                LogFailed(exception);\n                throw;\n            }\n',
                   '            catch (Exception)\n            {\n                throw;\n            }\n')]),
    ('M12b', 'a Consume that throws left unlogged', KAFKA_TESTS, [
        (CONSUMER, '        catch (ConsumeException exception)\n        {\n            LogFailedConsume(exception);\n            throw;\n        }',
                   '        catch (ConsumeException)\n        {\n            throw;\n        }')]),
    ('M12c', 'a ProduceAsync that throws left unlogged', KAFKA_TESTS, [
        (PRODUCER, '            _tracker.LogProduce(call, Failed(KafkaOperation.ProduceAsync, topic, exception), content, DateTimeOffset.UtcNow, Status(exception), Describe(exception));\n',
                   '')]),
    ('M12d', 'a failure status named by the exception type, not the error code', KAFKA_TESTS, [
        (PRODUCER, '        exception is KafkaException kafka ? "!" + kafka.Error.Code : FailedSend.Status(exception);',
                   '        FailedSend.Status(exception);')]),
    ('M12e', 'a cancelled wait not said to be possibly delivered', KAFKA_TESTS, [
        (PRODUCER, '        ? FailedSend.Describe(exception) + " The message was queued before the wait was cancelled, so it may still be delivered."',
                   '        ? FailedSend.Describe(exception)')]),
    ('M14', 'the clear moved back after the null/EOF return', KAFKA_TESTS, [
        (CONSUMER, '        if (_options.PropagateTestIdentity)\n            TestIdentityScope.ClearMessageIdentity();\n\n        ConsumeResult<TKey, TValue> result;',
                   '        ConsumeResult<TKey, TValue> result;'),
        (CONSUMER, '        if (result is null || result.IsPartitionEOF) return;\n\n        EstablishTestIdentity(result.Message?.Headers);',
                   '        if (result is null || result.IsPartitionEOF) return;\n\n        if (_options.PropagateTestIdentity) TestIdentityScope.ClearMessageIdentity();\n        EstablishTestIdentity(result.Message?.Headers);')]),
    ('M19', 'any InvalidOperationException taken for a refusal', KAFKA_TESTS, [
        (PRODUCER, '        && DeliveryReports == Unknown\n        && exception.GetType() == typeof(InvalidOperationException)\n        && exception.Message == DeliveryReportsDisabled;',
                   '        && DeliveryReports == Unknown;')]),
    ('M20', "a dependent producer's delivery setting read from itself, not its owner", KAFKA_TESTS, [
        (PRODUCER, '            var owner = HandleOwner?.GetValue(producer.Handle);', '            object? owner = producer;')]),
    ('M22', 'a producer known to have delivery reports off still handed a handler', KAFKA_TESTS, [
        (PRODUCER, '        if (DeliveryReports == Off)\n', '        if (DeliveryReports == Off && false)\n')]),
    ('M23', 'a one-instant consume stamped twice', KAFKA_TESTS, [
        (TRACKER, '        Log(call, op, content, incoming: true, call.At, failureStatus, error);',
                  '        Log(call, op, content, incoming: true, DateTimeOffset.UtcNow, failureStatus, error);')]),
    ('M24', 'content built only when the base verbosity shows it', KAFKA_TESTS, [
        (TRACKER, '        options.Verbosity != KafkaTrackingVerbosity.Summarised\n        || options.SetupVerbosity is { } setup && setup != KafkaTrackingVerbosity.Summarised\n        || options.ActionVerbosity is { } action && action != KafkaTrackingVerbosity.Summarised;',
                  '        options.Verbosity != KafkaTrackingVerbosity.Summarised;')]),
    ('M25', 'the Raw label keeps its trailing space', KAFKA_TESTS, [
        (CLASSIFIER, '(op.Topic is null ? op.Operation.ToString() : $"{op.Operation} {op.Topic}") +', '$"{op.Operation} {op.Topic}" +')]),
    ('M26', 'a subscribed sequence read twice', KAFKA_TESTS, [
        (CONSUMER, '        var subscribed = topics.ToList();\n        _inner.Subscribe(subscribed);', '        var subscribed = topics;\n        _inner.Subscribe(topics);')]),
    ('M27', 'Consume(int) not logged', KAFKA_TESTS, [
        (CONSUMER, '    public ConsumeResult<TKey, TValue> Consume(int millisecondsTimeout) => ConsumeCore(() => _inner.Consume(millisecondsTimeout));',
                   '    public ConsumeResult<TKey, TValue> Consume(int millisecondsTimeout) => _inner.Consume(millisecondsTimeout);')]),
    ('M28', 'ProduceAsync(TopicPartition) not logged', KAFKA_TESTS, [
        (PRODUCER, '        => ProduceAsync(topicPartition.Topic, (sent, token) => _inner.ProduceAsync(topicPartition, sent, token), message, cancellationToken);',
                   '        => _inner.ProduceAsync(topicPartition, message, cancellationToken);')]),
    ('M29', 'a consumed message not correlated', KAFKA_TESTS, [
        (CONSUMER, '        AutoCorrelateOnConsume(result);\n', '')]),
    ('M30', 'a setup-phase call tracked whatever TrackDuringSetup says', KAFKA_TESTS, [
        (TRACKER, '        TestPhase.Setup => _options.TrackDuringSetup,', '        TestPhase.Setup => true,')]),
    ('M31', 'Unassign not delegated', KAFKA_TESTS, [
        (CONSUMER, '    public void Unassign() => _inner.Unassign();', '    public void Unassign() { }')]),
    ('M32', "setup diagnostics not merged into the run's", CORE_TESTS, [
        (GENERATOR, '        if (ReportDiagnosticsScope.Current is { } setupDiagnostics)\n            ProcessDiagnostics.AddTo(setupDiagnostics);\n', '')]),
    ('M33', 'a setup diagnostic added again to a collector that holds it', CORE_TESTS, [
        (PROCESS, '            if (!held.Contains(entry))\n                collector.Add(entry);', '            collector.Add(entry);')]),
]


def stop_leftovers(worktree):
    """A mutation can leave the xUnit v3 test process running after dotnet test returns (M1 did: its host held the
    build output, so the next mutation's build could not copy and would read as a survivor). Stop whatever still runs
    from the worktree's bin directories."""
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
                assert text.count(old) == 1, f'{mid}: {path} holds the text {text.count(old)} times'
                with open(full, 'w', encoding='utf-8', newline='') as f:
                    f.write(text.replace(old, new))
            command = ['dotnet', 'test', project, '-f', 'net10.0', '-nologo', '--logger', 'console;verbosity=normal']
            if test_filter:
                command += ['--filter', test_filter]
            try:
                output = subprocess.run(command, cwd=worktree, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=600).stdout
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
