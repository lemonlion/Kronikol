MERGE = 'FullyQualifiedName~MergeReadsEveryTrackersMethodTests|FullyQualifiedName~MergedRunHistoryTests'
GUARD = 'FullyQualifiedName~DatabaseCallsAreCallsSourceTests'
LINE = '            RequestResponseType.Request, traceId, requestResponseId, false,\n'
BACK = '            RequestResponseType.Request, traceId, requestResponseId, false,\n            MetaType: RequestResponseMetaType.Event,\n'

MUTATIONS = [
    ('ReadMethod back to any case', 'src/Kronikol/Reports/Merge/MergeableReportReader.cs',
     '=> method switch', '=> method.ToUpperInvariant() switch', [('Kronikol.Tests', 'net10.0', MERGE)]),
    ('its Event branch back', 'src/Kronikol/Reports/Merge/MergeableReportReader.cs',
     'OneOf<HttpMethod, string> parsedMethod = ReadMethod(method);',
     'OneOf<HttpMethod, string> parsedMethod = metaType == RequestResponseMetaType.Event ? method : ReadMethod(method);', [('Kronikol.Tests', 'net10.0', MERGE)]),
    ('MethodText back to upper-casing', 'src/Kronikol/Reports/ReportGenerator.cs',
     'HttpMethod verb => verb.Method,', 'HttpMethod verb => verb.ToString().ToUpperInvariant(),', [('Kronikol.Tests', 'net10.0', MERGE)]),
    ('the line back in Spanner', 'src/Kronikol.Extensions.Spanner/SpannerTracker.cs', LINE, BACK,
     [('Kronikol.Tests.Spanner', None, None), ('Kronikol.Tests', 'net10.0', GUARD), ('Kronikol.Tests.EndToEnd', None, 'FullyQualifiedName~DatabaseAndEventCardsTests')]),
    ('the line back in BigQuery', 'src/Kronikol.Extensions.BigQuery/BigQueryTracker.cs', LINE, BACK,
     [('Kronikol.Tests.BigQuery', None, None), ('Kronikol.Tests', 'net10.0', GUARD)]),
    ('the line back in Bigtable', 'src/Kronikol.Extensions.Bigtable/BigtableTracker.cs', LINE, BACK,
     [('Kronikol.Tests.Bigtable', None, None), ('Kronikol.Tests', 'net10.0', GUARD)]),
    ('the guard reads no category', 'tests/Kronikol.Tests/Tracking/DatabaseCallsAreCallsSourceTests.cs',
     '.Where(statements.Contains)', '.Where(_ => false)', [('Kronikol.Tests', 'net10.0', GUARD)]),
    ('the guard looks for another word', 'tests/Kronikol.Tests/Tracking/DatabaseCallsAreCallsSourceTests.cs',
     'private const string EventKind = "RequestResponseMetaType.Event";', 'private const string EventKind = "RequestResponseMetaType.Evnt";', [('Kronikol.Tests', 'net10.0', GUARD)]),
]
