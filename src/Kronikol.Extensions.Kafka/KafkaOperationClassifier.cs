namespace Kronikol.Extensions.Kafka;

/// <summary>
/// Turns a Kafka client operation into the label and URI a diagram draws for it, at each verbosity.
/// </summary>
public static class KafkaOperationClassifier
{
    public static string GetDiagramLabel(KafkaOperationInfo op, KafkaTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            KafkaTrackingVerbosity.Raw =>
                (op.Topic is null ? op.Operation.ToString() : $"{op.Operation} {op.Topic}") +
                (op.Partition.HasValue ? $"[{op.Partition}]" : "") +
                (op.Offset.HasValue ? $"@{op.Offset}" : ""),
            KafkaTrackingVerbosity.Detailed => op.Operation switch
            {
                KafkaOperation.Produce or KafkaOperation.ProduceAsync =>
                    $"Produce → {op.Topic}",
                KafkaOperation.Consume =>
                    $"Consume ← {op.Topic}",
                KafkaOperation.Subscribe => $"Subscribe {op.Topic}",
                _ => op.Operation.ToString()
            },
            KafkaTrackingVerbosity.Summarised => op.Operation switch
            {
                KafkaOperation.Produce or KafkaOperation.ProduceAsync => "Produce",
                KafkaOperation.Consume => "Consume",
                KafkaOperation.InitTransactions => "Init Txn",
                KafkaOperation.BeginTransaction => "Begin Txn",
                KafkaOperation.CommitTransaction => "Commit Txn",
                KafkaOperation.AbortTransaction => "Abort Txn",
                KafkaOperation.SendOffsetsToTransaction => "Send Offsets",
                _ => op.Operation.ToString()
            },
            _ => op.Operation.ToString()
        };
    }

    public static Uri BuildUri(KafkaOperationInfo op, KafkaTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            KafkaTrackingVerbosity.Raw when op.Topic is not null =>
                new Uri($"kafka:///{op.Topic}" +
                    (op.Partition.HasValue ? $"/{op.Partition}" : "") +
                    (op.Offset.HasValue ? $"@{op.Offset}" : "")),
            KafkaTrackingVerbosity.Detailed when op.Topic is not null =>
                new Uri($"kafka:///{op.Topic}"),
            _ => new Uri("kafka:///")
        };
    }
}