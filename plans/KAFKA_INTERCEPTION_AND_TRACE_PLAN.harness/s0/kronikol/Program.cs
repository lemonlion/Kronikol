// Throwaway probe against the SHIPPED Kronikol.Extensions.Kafka(.BuildInterception) 4.9.0 packages.
using System.Reflection;
using System.Runtime.InteropServices;
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.Kafka.BuildInterception;
using Kronikol.Tracking;

var mode = args.Length > 0 ? args[0] : "pairs";
Console.WriteLine($"== mode={mode} runtime={RuntimeInformation.FrameworkDescription} " +
                  $"Kronikol.Extensions.Kafka.BuildInterception={typeof(KafkaBuildInterceptor).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} " +
                  $"Confluent.Kafka={typeof(ProducerBuilder<,>).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");

var current = new AsyncLocal<(string Name, string Id)?>();
void Options(KafkaTrackingOptions o)
{
    o.ServiceName = "Kafka";
    o.CallerName = "App";
    o.CurrentTestInfoFetcher = () => current.Value ?? ("<no test>", "<no test>");
}

ProducerConfig Cfg(bool deliveryReports = true) => new()
{
    BootstrapServers = "localhost:1",
    MessageTimeoutMs = 1000,
    EnableDeliveryReports = deliveryReports,
};

void Try<TKey, TValue>(string label, Message<TKey, TValue> message)
{
    try
    {
        using var p = new ProducerBuilder<TKey, TValue>(Cfg()).SetLogHandler((_, _) => { }).Build();
        object o = p;
        Console.WriteLine($"  {label}.Build() -> {o.GetType()}  isinst IProducer<{typeof(TKey).Name},{typeof(TValue).Name}>={o is IProducer<TKey, TValue>}");
        try { p.Produce("probe-topic", message); Console.WriteLine("    Produce -> ok"); }
        catch (Exception e) { Console.WriteLine($"    Produce -> {e.GetType().FullName}: {e.Message}"); }
    }
    catch (Exception e) { Console.WriteLine($"  {label}: {e.GetType().FullName}: {e.Message}"); }
}

void AllPairs()
{
    Try("ProducerBuilder<string,string>", new Message<string, string> { Value = "v" });
    Try("ProducerBuilder<Null,string>", new Message<Null, string> { Value = "v" });
    Try("ProducerBuilder<string,byte[]>", new Message<string, byte[]> { Value = [1] });
    Try("ProducerBuilder<int,string>", new Message<int, string> { Key = 1, Value = "v" });
}

switch (mode)
{
    case "pairs":
        Console.WriteLine("-- KafkaBuildInterceptor.EnableProducerTracking<string,string>():");
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(Options);
        AllPairs();
        Console.WriteLine("-- then EnableProducerTracking<Null,string>() as well:");
        KafkaBuildInterceptor.EnableProducerTracking<Null, string>(Options);
        AllPairs();
        Console.WriteLine("-- then KafkaBuildInterceptor.Reset():");
        KafkaBuildInterceptor.Reset();
        AllPairs();
        break;

    case "tierup":
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(Options);
        int tracked = 0, firstUntracked = -1;
        for (var i = 0; i < 300; i++)
        {
            using var p = new ProducerBuilder<string, string>(Cfg()).SetLogHandler((_, _) => { }).Build();
            if (p is TrackingKafkaProducer<string, string>) tracked++;
            else if (firstUntracked < 0) firstUntracked = i;
            if (i % 50 == 0) await Task.Delay(50);
        }
        Console.WriteLine($"300 x new ProducerBuilder<string,string>(...).Build() after EnableProducerTracking<string,string>: tracked={tracked} firstUntrackedAt={firstUntracked}");
        break;
    }

    case "attribution":
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(Options);
        current.Value = ("Test A (built the producer)", "A");
        var producer = new ProducerBuilder<string, string>(Cfg()).SetLogHandler((_, _) => { }).Build();
        Console.WriteLine($"built {producer.GetType().Name} under test A");
        current.Value = ("Test B (called Produce)", "B");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        producer.Produce("probe-topic", new Message<string, string> { Key = "k", Value = "v" }, dr => done.TrySetResult());
        await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
        current.Value = ("Test C (called ProduceAsync)", "C");
        try { await producer.ProduceAsync("probe-topic", new Message<string, string> { Key = "k", Value = "v" }); Console.WriteLine("ProduceAsync ok"); }
        catch (Exception e) { Console.WriteLine($"ProduceAsync threw {e.GetType().Name} ({(e as KafkaException)?.Error.Code})"); }
        current.Value = null;
        foreach (var log in RequestResponseLogger.RequestAndResponseLogs)
            Console.WriteLine($"  logged: {log.Type,-8} test='{log.TestName}' id={log.TestId} label={log.Method} uri={log.Uri} activityTraceId={log.ActivityTraceId ?? "<null>"}");
        Console.WriteLine($"  ({RequestResponseLogger.RequestAndResponseLogs.Length} entries in all)");
        producer.Dispose();
        break;
    }

    case "dr-disabled":
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(Options);
        using var p = new ProducerBuilder<string, string>(Cfg(deliveryReports: false)).SetLogHandler((_, _) => { }).Build();
        Console.WriteLine($"built {p.GetType().Name} with EnableDeliveryReports=false");
        try { p.Produce("probe-topic", new Message<string, string> { Value = "v" }); Console.WriteLine("Produce (no handler) -> ok"); }
        catch (Exception e) { Console.WriteLine($"Produce (no handler) -> {e.GetType().FullName}: {e.Message}"); }
        KafkaBuildInterceptor.Reset();
        using var plain = new ProducerBuilder<string, string>(Cfg(deliveryReports: false)).SetLogHandler((_, _) => { }).Build();
        try { plain.Produce("probe-topic", new Message<string, string> { Value = "v" }); Console.WriteLine($"same call on an unwrapped {plain.GetType().Name} -> ok"); }
        catch (Exception e) { Console.WriteLine($"unwrapped -> {e.GetType().FullName}: {e.Message}"); }
        break;
    }
}
Console.WriteLine("== done");
