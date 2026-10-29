// R2a's fourth test for Kronikol.Extensions.PubSub (plans/CLIENT_FLOORS_PLAN.md 5.1, 5.2): the real
// Google.Cloud.PubSub.V1 clients, wrapped by TrackingPublisherClient and TrackingSubscriberClient, against the Pub/Sub
// emulator (PUBSUB_EMULATOR_HOST), on the floor the extension asks for (no property) or on the newest 3.x
// (-p:Newest=true). Prints each call's outcome and what Kronikol recorded.
// Usage: PUBSUB_EMULATOR_HOST=127.0.0.1:8085 dotnet run
using System.Reflection;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Kronikol.Extensions.PubSub;
using Kronikol.Tracking;

var version = typeof(PublisherClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
Console.WriteLine($"# Google.Cloud.PubSub.V1 {version.Split('+')[0]} | emulator at {Environment.GetEnvironmentVariable("PUBSUB_EMULATOR_HOST")}");

var suffix = version.Split('+')[0].Replace('.', '-');
var topic = new TopicName("probe", "t-" + suffix);
var subscription = new SubscriptionName("probe", "s-" + suffix);
var options = new PubSubTrackingOptions { CurrentTestInfoFetcher = () => ("probe scenario", "probe-1") };

await Step("create the topic and subscription", async () =>
{
    var publisherApi = await new PublisherServiceApiClientBuilder { EmulatorDetection = EmulatorDetection.EmulatorOnly }.BuildAsync();
    await publisherApi.CreateTopicAsync(topic);
    var subscriberApi = await new SubscriberServiceApiClientBuilder { EmulatorDetection = EmulatorDetection.EmulatorOnly }.BuildAsync();
    await subscriberApi.CreateSubscriptionAsync(subscription, topic, pushConfig: null, ackDeadlineSeconds: 60);
    return "created";
});

PublisherClient? publisher = null;
await Step("publish through TrackingPublisherClient", async () =>
{
    var inner = await new PublisherClientBuilder { TopicName = topic, EmulatorDetection = EmulatorDetection.EmulatorOnly }.BuildAsync();
    publisher = new TrackingPublisherClient(inner, options, null);
    return await publisher.PublishAsync(new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") });
});

await Step("receive through TrackingSubscriberClient", async () =>
{
    var inner = await new SubscriberClientBuilder { SubscriptionName = subscription, EmulatorDetection = EmulatorDetection.EmulatorOnly }.BuildAsync();
    var subscriber = new TrackingSubscriberClient(inner, options, null);
    var received = new TaskCompletionSource<string>();
    var running = subscriber.StartAsync((message, _) =>
    {
        received.TrySetResult(message.Data.ToStringUtf8());
        return Task.FromResult(SubscriberClient.Reply.Ack);
    });
    var data = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
    await subscriber.StopAsync(TimeSpan.FromSeconds(10));
    await running;
    return data;
});

if (publisher is not null)
    await publisher.ShutdownAsync(TimeSpan.FromSeconds(10));

Console.WriteLine("## What Kronikol recorded");
foreach (var log in RequestResponseLogger.RequestAndResponseLogs)
    Console.WriteLine($"  {log.Type} {log.Method.Value} {log.Uri} {log.StatusCode?.Value} ({log.ServiceName})");

static async Task Step<T>(string name, Func<Task<T>> call)
{
    try
    {
        var result = await call();
        Console.WriteLine($"{name}: ok{(result is string s ? " " + s : "")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{name}: THROWS {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
    }
}
