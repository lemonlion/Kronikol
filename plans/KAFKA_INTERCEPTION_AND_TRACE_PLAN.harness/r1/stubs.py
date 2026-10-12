"""R1's compile stubs (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md 6.6): applied to the tree published before R1 so R1's
facts compile there and fail on their own assertions. Each stub adds a name and no behaviour: an option nothing reads, two
constants, the IVTs R1's facts need, and the Q17 helpers throwing NotImplementedException.

Usage: python stubs.py <worktree>   (then copy R1's test files in: git checkout kafka-135-r1 -- <paths>)
"""
import sys

ROOT = sys.argv[1]


def edit(path, old, new):
    full = f'{ROOT}/{path}'
    with open(full, encoding='utf-8', newline='') as f:
        text = f.read()
    if '\r\n' in text:  # some sources are stored with CRLF
        old, new = old.replace('\n', '\r\n'), new.replace('\n', '\r\n')
    assert text.count(old) == 1, (path, old[:60], text.count(old))
    with open(full, 'w', encoding='utf-8', newline='') as f:
        f.write(text.replace(old, new, 1))


edit('src/Kronikol.Extensions.Kafka/KafkaTrackingOptions.cs',
     '    public bool PropagateTestIdentity { get; set; } = true;\n',
     '    public bool PropagateTestIdentity { get; set; } = true;\n\n'
     '    // R1 stub: read by nothing.\n'
     '    public bool PropagateTraceContext { get; set; } = true;\n')

edit('src/Kronikol/Constants/HttpHeaders.cs',
     'public const string TestId = "kronikol-test-id";',
     'public const string TestId = "kronikol-test-id";\n\n    // R1 stub: written by nothing.\n    public const string TraceId = "kronikol-trace-id";')

edit('src/Kronikol/Kronikol.csproj',
     '</Project>',
     '  <ItemGroup>\n'
     '    <InternalsVisibleTo Include="Kronikol.Extensions.Kafka" />\n'
     '    <InternalsVisibleTo Include="Kronikol.Tests.Kafka" />\n'
     '  </ItemGroup>\n'
     '</Project>')

edit('src/Kronikol/PlantUml/PlantUmlCreator.cs',
     '    internal static string EscapeCreoleMarkup(string text)',
     '    // R1 stub: drawn by nothing.\n'
     '    internal const string NoteKeyLabel = "<b>Key</b> ";\n\n'
     '    internal static string EscapeCreoleMarkup(string text)')

edit('src/Kronikol.Extensions.Kafka/KafkaTracker.cs',
     '    public void LogSubscribe(KafkaOperationInfo op)',
     '    // R1 stubs (Q17): the helpers do not exist yet.\n'
     '    public KafkaProduceTracking<TKey, TValue> TrackProduce<TKey, TValue>(string topic, Confluent.Kafka.Message<TKey, TValue> message,\n'
     '        KafkaOperation operation = KafkaOperation.Produce) => throw new NotImplementedException("R1 stub");\n\n'
     '    public KafkaProduceTracking<TKey, TValue> TrackProduce<TKey, TValue>(Confluent.Kafka.TopicPartition topicPartition, Confluent.Kafka.Message<TKey, TValue> message,\n'
     '        KafkaOperation operation = KafkaOperation.Produce) => throw new NotImplementedException("R1 stub");\n\n'
     '    public IDisposable TrackConsume<TKey, TValue>(Confluent.Kafka.ConsumeResult<TKey, TValue>? result) => throw new NotImplementedException("R1 stub");\n\n'
     '    public void LogSubscribe(KafkaOperationInfo op)')

with open(f'{ROOT}/src/Kronikol.Extensions.Kafka/KafkaProduceTracking.cs', 'w', encoding='utf-8', newline='') as f:
    f.write('''using Confluent.Kafka;

namespace Kronikol.Extensions.Kafka;

// R1 stub (Q17): every member throws.
public sealed class KafkaProduceTracking<TKey, TValue>
{
    public Message<TKey, TValue> Message => throw new NotImplementedException("R1 stub");
    public void Delivered(DeliveryResult<TKey, TValue> result) => throw new NotImplementedException("R1 stub");
    public void Delivered(DeliveryReport<TKey, TValue> report) => throw new NotImplementedException("R1 stub");
    public void Failed(Exception exception) => throw new NotImplementedException("R1 stub");
    public void Sent() => throw new NotImplementedException("R1 stub");
    public Action<DeliveryReport<TKey, TValue>> Handler(Action<DeliveryReport<TKey, TValue>>? then = null) =>
        throw new NotImplementedException("R1 stub");
}
''')
print('stubs applied')
