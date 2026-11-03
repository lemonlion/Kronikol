CORE = [('Kronikol.Tests', 'net10.0', 'FullyQualifiedName~ShardPartialTests|FullyQualifiedName~MergeLeavesAttachmentsAloneTests|FullyQualifiedName~HistoryMaintenanceTests')]
M = 'src/Kronikol.Tool/HistoryCommand.Maintenance.cs'
MUTATIONS = [
    ('the fold drops the threshold', 'src/Kronikol/History/HistoryLedgerWriter.cs', '            threshold ??= run.PartialThreshold;', '', CORE),
    ('the analyzer ignores the threshold', 'src/Kronikol/History/HistoryAnalyzer.cs', 'current.PartialThreshold ?? options.PartialThreshold', 'options.PartialThreshold', CORE),
    ('every reserved folder counts as attachments', 'src/Kronikol/Reports/ReportFolders.cs', '''        IsReserved(root, file)
        && Path.GetRelativePath(root, file).Split''', '''        IsReserved(root, file)
        || Path.GetRelativePath(root, file).Split''', CORE),
    ('import sweeps a reserved folder', M, 'f => QueryCommand.IsReportFile(f) && !ReportFolders.IsAttachment(full, f)', 'f => QueryCommand.IsReportFile(f)', CORE),
    ('import of ctrf sweeps a reserved folder', M, '.Where(f => !ReportFolders.IsAttachment(full, f)).OrderBy', '.OrderBy', CORE),
]
