using Confluent.Kafka;

namespace Kronikol.Tests.Kafka;

/// <summary>
/// The fake producer the Kafka facts share. It records every message it is handed, as handed (the wrapper may hand it a
/// copy of the caller's), with the delivery handler, and can play a delivery report later on a thread that carries
/// another flow's context, the way Confluent's poll thread runs a handler under the context of whoever built the
/// producer (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md F4).
/// </summary>
internal sealed class FakeProducer<TKey, TValue> : IProducer<TKey, TValue>
{
    private readonly List<Produced> _produced = [];

    public sealed record Produced(string Topic, Message<TKey, TValue> Message, Action<DeliveryReport<TKey, TValue>>? Handler);

    /// <summary>Every produce, in order, with the message and handler the wrapper handed over.</summary>
    public IReadOnlyList<Produced> ProducedMessages
    {
        get
        {
            lock (_produced) return _produced.ToArray();
        }
    }

    /// <summary>Thrown by the next produce, sync or async, after the message is recorded; cleared once thrown.</summary>
    public Exception? ThrowOnNextProduce { get; set; }

    /// <summary>Decides, per produce, whether to refuse it: return the exception to throw, or null to accept.</summary>
    public Func<Action<DeliveryReport<TKey, TValue>>?, Exception?>? Refuse { get; set; }

    /// <summary>What <see cref="ProduceAsync(string, Message{TKey,TValue}, CancellationToken)"/> completes with.</summary>
    public Func<string, Message<TKey, TValue>, CancellationToken, Task<DeliveryResult<TKey, TValue>>>? ProduceAsyncResult { get; set; }

    public bool FlushCalled { get; private set; }
    public bool InitTransactionsCalled { get; private set; }
    public bool BeginTransactionCalled { get; private set; }
    public bool CommitTransactionCalled { get; private set; }
    public bool AbortTransactionCalled { get; private set; }
    public bool SendOffsetsCalled { get; private set; }

    public Handle Handle => throw new NotImplementedException();
    public string Name => "fake";
    public void SetSaslCredentials(string username, string password) { }
    public void Dispose() { }
    public int AddBrokers(string brokers) => 0;
    public void InitTransactions(TimeSpan timeout) { InitTransactionsCalled = true; }
    public void BeginTransaction() { BeginTransactionCalled = true; }
    public void CommitTransaction(TimeSpan timeout) { CommitTransactionCalled = true; }
    public void CommitTransaction() { CommitTransactionCalled = true; }
    public void AbortTransaction(TimeSpan timeout) { AbortTransactionCalled = true; }
    public void AbortTransaction() { AbortTransactionCalled = true; }
    public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) { SendOffsetsCalled = true; }
    public int Flush(TimeSpan timeout) { FlushCalled = true; return 0; }
    public void Flush(CancellationToken cancellationToken = default) { FlushCalled = true; }
    public int Poll(TimeSpan timeout) => 0;

    public void Produce(string topic, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null) =>
        Record(topic, message, deliveryHandler);

    public void Produce(TopicPartition topicPartition, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null) =>
        Record(topicPartition.Topic, message, deliveryHandler);

    public Task<DeliveryResult<TKey, TValue>> ProduceAsync(string topic, Message<TKey, TValue> message, CancellationToken cancellationToken = default)
    {
        Record(topic, message, null);
        return ProduceAsyncResult?.Invoke(topic, message, cancellationToken)
            ?? Task.FromResult(new DeliveryResult<TKey, TValue> { Topic = topic, Partition = new Partition(0), Offset = new Offset(0), Message = message });
    }

    public Task<DeliveryResult<TKey, TValue>> ProduceAsync(TopicPartition topicPartition, Message<TKey, TValue> message, CancellationToken cancellationToken = default)
    {
        Record(topicPartition.Topic, message, null);
        return ProduceAsyncResult?.Invoke(topicPartition.Topic, message, cancellationToken)
            ?? Task.FromResult(new DeliveryResult<TKey, TValue> { Topic = topicPartition.Topic, Partition = topicPartition.Partition, Offset = new Offset(0), Message = message });
    }

    /// <summary>
    /// Plays the delivery report of produce <paramref name="index"/> to its handler on a new thread running in
    /// <paramref name="context"/>, the context Confluent's poll thread inherited, and waits for it.
    /// </summary>
    public void Deliver(int index, DeliveryReport<TKey, TValue> report, ExecutionContext context)
    {
        var handler = ProducedMessages[index].Handler ?? throw new InvalidOperationException($"Produce {index} was handed no delivery handler.");
        Exception? thrown = null;
        var thread = new Thread(() => ExecutionContext.Run(context, _ =>
        {
            try { handler(report); }
            catch (Exception e) { thrown = e; }
        }, null));
        thread.Start();
        thread.Join();
        if (thrown is not null)
            throw new InvalidOperationException("The delivery handler threw.", thrown);
    }

    private void Record(string topic, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>>? handler)
    {
        lock (_produced) _produced.Add(new Produced(topic, message, handler));
        if (Refuse?.Invoke(handler) is { } refusal)
            throw refusal;
        if (ThrowOnNextProduce is { } exception)
        {
            ThrowOnNextProduce = null;
            throw exception;
        }
    }
}

/// <summary>
/// A consumer whose polls return, in order, what the fact queued: a result, null (an empty poll) or an exception.
/// </summary>
internal sealed class ScriptedConsumer<TKey, TValue> : IConsumer<TKey, TValue>
{
    private readonly Queue<Func<ConsumeResult<TKey, TValue>?>> _polls = new();

    public ScriptedConsumer<TKey, TValue> Returns(ConsumeResult<TKey, TValue>? result)
    {
        _polls.Enqueue(() => result);
        return this;
    }

    public ScriptedConsumer<TKey, TValue> Throws(Exception exception)
    {
        _polls.Enqueue(() => throw exception);
        return this;
    }

    /// <summary>Topics handed to <see cref="Subscribe(IEnumerable{string})"/>, as the inner consumer enumerated them.</summary>
    public List<string> Subscribed { get; } = [];

    public int Polls { get; private set; }

    private ConsumeResult<TKey, TValue> Next()
    {
        Polls++;
        return (_polls.Count > 0 ? _polls.Dequeue()() : null)!;
    }

    public ConsumeResult<TKey, TValue> Consume(int millisecondsTimeout) => Next();
    public ConsumeResult<TKey, TValue> Consume(CancellationToken cancellationToken = default) => Next();
    public ConsumeResult<TKey, TValue> Consume(TimeSpan timeout) => Next();

    public Handle Handle => throw new NotImplementedException();
    public string Name => "scripted";
    public string MemberId => "scripted";
    public List<TopicPartition> Assignment => [];
    public List<string> Subscription => [];
    public IConsumerGroupMetadata ConsumerGroupMetadata => throw new NotImplementedException();
    public void SetSaslCredentials(string username, string password) { }
    public void Dispose() { }
    public int AddBrokers(string brokers) => 0;
    public void Subscribe(IEnumerable<string> topics) => Subscribed.AddRange(topics);
    public void Subscribe(string topic) => Subscribed.Add(topic);
    public void Unsubscribe() { }
    public void Assign(TopicPartition partition) { }
    public void Assign(TopicPartitionOffset partition) { }
    public void Assign(IEnumerable<TopicPartition> partitions) { }
    public void Assign(IEnumerable<TopicPartitionOffset> partitions) { }
    public void IncrementalAssign(IEnumerable<TopicPartitionOffset> partitions) { }
    public void IncrementalAssign(IEnumerable<TopicPartition> partitions) { }
    public void IncrementalUnassign(IEnumerable<TopicPartition> partitions) { }
    public void Unassign() { }
    public void StoreOffset(ConsumeResult<TKey, TValue> result) { }
    public void StoreOffset(TopicPartitionOffset offset) { }
    public List<TopicPartitionOffset> Commit() => [];
    public void Commit(IEnumerable<TopicPartitionOffset> offsets) { }
    public void Commit(ConsumeResult<TKey, TValue> result) { }
    public void Seek(TopicPartitionOffset tpo) { }
    public void Pause(IEnumerable<TopicPartition> partitions) { }
    public void Resume(IEnumerable<TopicPartition> partitions) { }
    public List<TopicPartitionOffset> Committed(TimeSpan timeout) => [];
    public List<TopicPartitionOffset> Committed(IEnumerable<TopicPartition> partitions, TimeSpan timeout) => [];
    public Offset Position(TopicPartition partition) => Offset.Unset;
    public List<TopicPartitionOffset> OffsetsForTimes(IEnumerable<TopicPartitionTimestamp> timestampsToSearch, TimeSpan timeout) => [];
    public WatermarkOffsets GetWatermarkOffsets(TopicPartition topicPartition) => new(Offset.Unset, Offset.Unset);
    public WatermarkOffsets QueryWatermarkOffsets(TopicPartition topicPartition, TimeSpan timeout) => new(Offset.Unset, Offset.Unset);
    public void Close() { }
}
