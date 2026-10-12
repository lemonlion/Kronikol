using System.Reflection;
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md F17: every member of IProducer and IConsumer reaches the client the wrapper
// wraps, so a member the wrapper forgets, or answers itself, shows here.
public class KafkaClientDelegationTests
{
    private static readonly KafkaTrackingOptions Options = new()
    {
        CurrentTestInfoFetcher = () => ("Delegation Test", "delegation-test"),
        TrackProduce = false,
        TrackConsume = false,
    };

    public static TheoryData<string> ProducerMembers => [.. Members(typeof(IProducer<string, string>))];

    public static TheoryData<string> ConsumerMembers => [.. Members(typeof(IConsumer<string, string>))];

    [Theory]
    [MemberData(nameof(ProducerMembers))]
    public void Every_producer_member_reaches_the_wrapped_producer(string member)
    {
        var inner = DispatchProxy.Create<IProducer<string, string>, RecordingClient>();
        var wrapper = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(Options), Options);

        Invoke(wrapper, typeof(IProducer<string, string>), member);

        Assert.Contains(member, ((RecordingClient)(object)inner).Calls);
    }

    [Theory]
    [MemberData(nameof(ConsumerMembers))]
    public void Every_consumer_member_reaches_the_wrapped_consumer(string member)
    {
        var inner = DispatchProxy.Create<IConsumer<string, string>, RecordingClient>();
        var wrapper = new TrackingKafkaConsumer<string, string>(inner, new KafkaTracker(Options), Options);

        Invoke(wrapper, typeof(IConsumer<string, string>), member);

        Assert.Contains(member, ((RecordingClient)(object)inner).Calls);
    }

    // Each method of the interface and the interfaces it extends, named with its parameter types so overloads differ.
    private static IEnumerable<string> Members(Type client) =>
        AllMethods(client).Select(Signature).Distinct();

    private static IEnumerable<MethodInfo> AllMethods(Type client) =>
        new[] { client }.Concat(client.GetInterfaces()).SelectMany(i => i.GetMethods());

    private static string Signature(MethodInfo method) =>
        $"{method.Name}({string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name))})";

    private static void Invoke(object wrapper, Type client, string member)
    {
        var method = AllMethods(client).First(m => Signature(m) == member);
        var arguments = method.GetParameters().Select(p => Argument(p.ParameterType)).ToArray();
        var result = method.Invoke(wrapper, arguments);
        if (result is Task task)
            task.GetAwaiter().GetResult();
    }

    private static object? Argument(Type type) =>
        type == typeof(string) ? "orders"
        : type == typeof(Message<string, string>) ? new Message<string, string> { Key = "k", Value = "v" }
        : type == typeof(TopicPartition) ? new TopicPartition("orders", new Partition(0))
        : type == typeof(TopicPartitionOffset) ? new TopicPartitionOffset("orders", new Partition(0), new Offset(0))
        : type == typeof(ConsumeResult<string, string>) ? new ConsumeResult<string, string> { TopicPartitionOffset = new("orders", new Partition(0), new Offset(0)), Message = new Message<string, string>() }
        : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? Array.CreateInstance(type.GetGenericArguments()[0], 0)
        : type.IsValueType ? Activator.CreateInstance(type)
        : null;

    /// <summary>Records each member called on it and answers with a harmless value.</summary>
    public class RecordingClient : DispatchProxy
    {
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(Signature(targetMethod!));
            var returns = targetMethod!.ReturnType;
            if (returns == typeof(Task<DeliveryResult<string, string>>))
                return Task.FromResult(new DeliveryResult<string, string> { Topic = "orders", Partition = new Partition(0), Offset = new Offset(0) });
            if (returns == typeof(ConsumeResult<string, string>))
                return null;
            if (returns == typeof(string))
                return "recorded";
            if (returns.IsGenericType && returns.GetGenericTypeDefinition() == typeof(List<>))
                return Activator.CreateInstance(returns);
            if (returns == typeof(WatermarkOffsets))
                return new WatermarkOffsets(Offset.Unset, Offset.Unset);
            return returns.IsValueType && returns != typeof(void) ? Activator.CreateInstance(returns) : null;
        }
    }
}
