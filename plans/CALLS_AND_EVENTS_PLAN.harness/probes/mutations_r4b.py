CMD = 'src/Kronikol.Tool/HistoryCommand.cs'
FOLD = 'src/Kronikol/History/HistoryLedgerWriter.cs'
CTX = 'src/Kronikol/History/HistoryRunContext.cs'
MERGER = 'src/Kronikol/Reports/Merge/MergeableReportMerger.cs'
CORE = [('Kronikol.Tests', 'net10.0', 'FullyQualifiedName~ShardPartialTests|FullyQualifiedName~RelativeAddressRunTests|FullyQualifiedName~MergeKeepsEveryScenarioTests|FullyQualifiedName~MergeLeavesAttachmentsAloneTests|FullyQualifiedName~MergedRunHistoryTests')]

MUTATIONS = [
    ('a line is read against every branch', CMD, 'held.PriorRuns(run.Suite, run.Stream, run.Id, DefaultWindow)', 'held.Runs(run.Suite)', CORE),
    ('the threshold is not read at record', CMD, 'line.PartialThreshold ?? 0.10', '0.10', CORE),
    ('the fragment drops the threshold', CTX, 'HostThreshold = options.HistoryPartialThreshold is 0.10 ? null : options.HistoryPartialThreshold', 'HostThreshold = null', CORE),
    ('the fragment always names a threshold', CTX, 'HostThreshold = options.HistoryPartialThreshold is 0.10 ? null : options.HistoryPartialThreshold', 'HostThreshold = options.HistoryPartialThreshold', CORE),
    ('the fold drops the threshold', FOLD, '            threshold ??= run.PartialThreshold;', '', CORE),
    ('the analyzer ignores the threshold', 'src/Kronikol/History/HistoryAnalyzer.cs', 'current.PartialThreshold ?? options.PartialThreshold', 'options.PartialThreshold', CORE),
    ('every shard saying no is not heard', FOLD, 'partial ?? (everyShardSaidNo ? false : null)', 'partial', CORE),
    ('one shard saying no is enough', FOLD, 'everyShardSaidNo &= run.Partial == false;', 'everyShardSaidNo &= run.Partial != true;', CORE),
    ('a relative address gets its end space escaped', 'src/Kronikol/Reports/Merge/MergeableReportReader.cs', '!RecordedUri.TryAbsolute(address, out _)', 'false', CORE),
    ('a copy is kept', MERGER, 'if (held <= keptOf.GetValueOrDefault(identity))', 'if (held < keptOf.GetValueOrDefault(identity))', CORE),
    ('a look-alike counts one', MERGER, 'keptOf[identity] = held;', 'keptOf[identity] = 1;', CORE),
    ('merge says nothing of attachments', 'src/Kronikol.Tool/MergeCommand.cs', 'underAttachments += found.Count(f => ReportFolders.IsAttachment(input, f));', '', CORE),
    ('every reserved folder counts as attachments', 'src/Kronikol/Reports/ReportFolders.cs', '''        IsReserved(root, file)
        && Path.GetRelativePath(root, file).Split''', '''        IsReserved(root, file)
        || Path.GetRelativePath(root, file).Split''', CORE),
    ('history record sweeps a reserved folder', CMD, 'else if (!ReportFolders.IsReserved(full, file))', 'else', CORE),
    ('any scheme reads absolute', 'src/Kronikol/Tracking/RecordedUri.cs', 'address.Contains("://", StringComparison.Ordinal) && ', '', CORE),
    ('nothing reads absolute', 'src/Kronikol/Tracking/RecordedUri.cs', '&& Uri.TryCreate(address, UriKind.Absolute, out uri)', '&& false', CORE),
]
