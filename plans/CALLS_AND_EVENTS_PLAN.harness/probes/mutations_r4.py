PARTIAL = 'FullyQualifiedName~ShardPartialTests'
MERGED = 'FullyQualifiedName~MergedRunHistoryTests'
KEEPS = 'FullyQualifiedName~MergeKeepsEveryScenarioTests'
FOLDER = 'FullyQualifiedName~MergeLeavesAttachmentsAloneTests|FullyQualifiedName~RunManifestTests'
RELATIVE = 'FullyQualifiedName~RelativeAddressRunTests'

MUTATIONS = [
    ('a fragment carries the shard\'s own reading', 'src/Kronikol/History/HistoryRunContext.cs',
     'HistoryFragment.Write(Roster, Run with { Partial = HostPartial }, Generator', 'HistoryFragment.Write(Roster, Run, Generator', [('Kronikol.Tests', 'net10.0', PARTIAL)]),
    ('a fragment forgets what the host said', 'src/Kronikol/History/HistoryRunContext.cs',
     '{ HostPartial = options.HistoryPartialRun }', '{ HostPartial = null }', [('Kronikol.Tests', 'net10.0', PARTIAL)]),
    ('an address read back with its end spaces', 'src/Kronikol/Reports/Merge/MergeableReportReader.cs',
     'Uri.TryCreate(EndSpacesEscaped(GetString(element, "uri")), UriKind', 'Uri.TryCreate(GetString(element, "uri"), UriKind', [('Kronikol.Tests', 'net10.0', MERGED)]),
    ('a report\'s own rows read as a copy', 'src/Kronikol/Reports/Merge/MergeableReportMerger.cs',
     'seen.TryGetValue(identity, out var firstIn) && firstIn != index', 'seen.TryGetValue(identity, out var firstIn)', [('Kronikol.Tests', 'net10.0', KEEPS)]),
    ('a later report\'s copy is kept', 'src/Kronikol/Reports/Merge/MergeableReportMerger.cs',
     'seen.TryGetValue(identity, out var firstIn) && firstIn != index', 'seen.TryGetValue(identity, out var firstIn) && firstIn == -1', [('Kronikol.Tests', 'net10.0', KEEPS)]),
    ('attachments are swept as shards', 'src/Kronikol/Reports/ReportFolders.cs',
     '|| string.Equals(segment, AttachmentsFolderName, StringComparison.OrdinalIgnoreCase));', ');', [('Kronikol.Tests', 'net10.0', FOLDER)]),
    ('the helper asks a relative address for its path and query', 'src/Kronikol/Tracking/RecordedUri.cs',
     'uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString', 'uri.PathAndQuery', [('Kronikol.Tests', 'net10.0', RELATIVE)]),
    ('the helper asks a relative address for its path', 'src/Kronikol/Tracking/RecordedUri.cs',
     'if (uri.IsAbsoluteUri)', 'if (uri.IsAbsoluteUri || uri.OriginalString.Length >= 0)', [('Kronikol.Tests', 'net10.0', RELATIVE)]),
    ('the query is dropped from the drawn address', 'src/Kronikol/Tracking/RecordedUri.cs',
     'uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString', 'uri.IsAbsoluteUri ? uri.PathAndQuery : AbsolutePath(uri)', [('Kronikol.Tests', 'net10.0', RELATIVE)]),
    ('the drawing reader asks the address again', 'src/Kronikol/PlantUml/PlantUmlCreator.cs',
     'var fullPathAndQuery = RecordedUri.PathAndQuery(effectiveUri);', 'var fullPathAndQuery = effectiveUri.PathAndQuery;', [('Kronikol.Tests', 'net10.0', RELATIVE)]),
    ('the collapser asks the address again', 'src/Kronikol/PlantUml/SequenceCollapser.cs',
     'RecordedUri.PathAndQuery(request.Uri),', 'request.Uri.PathAndQuery,', [('Kronikol.Tests', 'net10.0', RELATIVE)]),
    ('the component flow asks the address again', 'src/Kronikol/ComponentDiagram/ComponentFlowSegmentBuilder.cs',
     'Path: RecordedUri.AbsolutePath(l.Uri))).ToArray();', 'Path: l.Uri.AbsolutePath)).ToArray();', [('Kronikol.Tests', 'net10.0', RELATIVE)]),
    ('the internal flow asks the address again', 'src/Kronikol/InternalFlow/InternalFlowSegmentBuilder.cs',
     '{RecordedUri.PathAndQuery(l.Uri)}', '{l.Uri.PathAndQuery}', [('Kronikol.Tests', 'net10.0', RELATIVE)]),
]
