M = 'src/Kronikol.Extensions.Otlp/OtlpSpanMapper.cs'
T = 'src/Kronikol.Extensions.Otlp/SpanToInteractionMapper.cs'
OTLP = [('Kronikol.Tests.Otlp', None, None)]
CARD = [('Kronikol.Tests', 'net10.0', 'FullyQualifiedName~CallGraph|FullyQualifiedName~MessageTable|FullyQualifiedName~ExportCommandTests')]
NEVER = 'log.DependencyCategory == "\\0"'

MUTATIONS = [
    ('step 1: a call at Default', M, '        if (log.MetaType != RequestResponseMetaType.Event)', '        if (' + NEVER + ')', OTLP),
    ('step 2: a statement written as an event', M, '        if (DependencyCategories.IsStatementShaped(log.DependencyCategory))', '        if (' + NEVER + ')', OTLP),
    ('step 3: an event on the web', M, '        if (IsHttpRequest(log))', '        if (' + NEVER + ')', OTLP),
    ('a delivery mark', M, '        if (log.NoteOnRight)', '        if (' + NEVER + ')', OTLP),
    ('a label its author wrote', M, '        if (log.LabelIsFree)', '        if (' + NEVER + ')', OTLP),
    ('a label read back', M, '        if (!log.ReadFromRecord && !fromASpan)', '        if (!fromASpan)', OTLP),
    ('Kafka reads as any other send', M, '_ => IsKafkasProduce(log) ? OtlpSpanKind.Producer : OtlpSpanKind.Client,', '_ => OtlpSpanKind.Client,', OTLP),
    ('every send reads PRODUCER', M, '_ => IsKafkasProduce(log) ? OtlpSpanKind.Producer : OtlpSpanKind.Client,', '_ => OtlpSpanKind.Producer,', OTLP),
    ('messaging on any scheme', M, '            || (log.MetaType != RequestResponseMetaType.Event && !MessageTable.HasScheme(scheme)))', '            )', OTLP),
    ('a wire record reads as the broker\'s arrow', M,
     'var drawnFromItsCaller = fromASpan || string.Equals(log.CapturedBy, InteractionMerger.WireSource, StringComparison.OrdinalIgnoreCase);',
     'var drawnFromItsCaller = fromASpan;', OTLP),
    ('the tap\'s Pub/Sub scheme is no system', M, '"pubsub" or "gcp-pubsub" => "gcp_pubsub",', '"pubsub" => "gcp_pubsub",', OTLP),
    ('the tap\'s SNS scheme is no system', M, '"sns" or "aws-sns" => "aws.sns",', '"sns" => "aws.sns",', OTLP),
    ('a span\'s destination read where a tracker writes it', M,
     '(fromASpan ? MessageTable.LastSegment(uri) : MessageTable.Destination(uri))', 'MessageTable.Destination(uri)', OTLP),
    ('two words read as one', M, 'if (second is "Fault" or "Offsets")', 'if (second is "\\0")', OTLP),
    ('http.request.method on any scheme', M, 'if (primary.Method.Value is HttpMethod httpMethod && IsOnTheWeb(primary))', 'if (primary.Method.Value is HttpMethod httpMethod)', OTLP),
    ('the old database name', M, 'DependencyCategories.Spanner => "gcp.spanner",', 'DependencyCategories.Spanner => "spanner",', OTLP),
    ('a map keyed on the other name', T, 'if (OtherNameOf(fallback) is { } other)', 'if (OtherNameOf("") is { } other)', OTLP),
    ('a settlement moves a message', T, '"settle" => false,', '"settle" => true,', OTLP),
    ('a CLIENT send is no message', T, '"send" or "publish" or "create" or "receive" or "process" or "deliver" => true,', '"publish" or "create" or "receive" or "process" or "deliver" => true,', OTLP),
    ('the card reads no delivery', 'src/Kronikol/CallGraph/CallGraphBuilder.cs',
     'MessageLabelKind.Receives or MessageLabelKind.Delivers => "consumes",', 'MessageLabelKind.Receives => "consumes",', CARD),
    ('a pair takes two traces', 'src/Kronikol/Tracking/MessageTracker.cs',
     'var pairTrace = requestResponseId == Guid.Empty ? Guid.NewGuid() : requestResponseId;', 'var pairTrace = Guid.NewGuid();', [('Kronikol.Tests', 'net10.0', 'FullyQualifiedName~MessageTracker')] + OTLP),
]
