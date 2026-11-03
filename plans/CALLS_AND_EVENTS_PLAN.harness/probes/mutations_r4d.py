CORE = [('Kronikol.Tests', 'net10.0', 'FullyQualifiedName~ShardPartialTests|FullyQualifiedName~HistoryMaintenanceTests|FullyQualifiedName~HistoryCommandTests|FullyQualifiedName~MergeLeavesAttachmentsAloneTests')]
C = 'src/Kronikol.Tool/HistoryCommand.cs'
M = 'src/Kronikol.Tool/HistoryCommand.Maintenance.cs'
MUTATIONS = [
    ('history record sweeps attachments', C, '!ReportFolders.IsAttachment(full, file) && !ReportFolders.IsBaseline(full, file)', '!ReportFolders.IsBaseline(full, file)', CORE),
    ('history record sweeps baseline', C, '!ReportFolders.IsAttachment(full, file) && !ReportFolders.IsBaseline(full, file)', '!ReportFolders.IsAttachment(full, file)', CORE),
    ('history record leaves runs too', C, '!ReportFolders.IsAttachment(full, file) && !ReportFolders.IsBaseline(full, file)', '!ReportFolders.IsReserved(full, file)', CORE),
    ('import leaves runs too', M, 'f => QueryCommand.IsReportFile(f) && !ReportFolders.IsAttachment(full, f)', 'f => QueryCommand.IsReportFile(f) && !ReportFolders.IsReserved(full, f)', CORE),
    ('import sweeps attachments', M, 'f => QueryCommand.IsReportFile(f) && !ReportFolders.IsAttachment(full, f)', 'f => QueryCommand.IsReportFile(f)', CORE),
    ('import of ctrf sweeps attachments', M, '.Where(f => !ReportFolders.IsAttachment(full, f)).OrderBy', '.OrderBy', CORE),
]
