using System.Xml.Linq;

namespace Kronikol.Tests.Kafka.BuildInterception;

// The doc comments ship in the packages, so a sentence about the old behaviour is something a consumer reads
// (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md F3, F4, F12): Reset() said it removed every patch, Disable…() that the
// patch became a no-op, the classifier that it read HTTP requests, and an option that a produced message carries its
// produce's scenario, which F4 made false. The reader is DocCommentPlacementTests'.
public class KafkaShippedDocsTests
{
    private static string Summary(string assembly, string member) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, assembly + ".xml"))
            .Descendants("member").Single(m => (string)m.Attribute("name")! == member)
            .Element("summary")!.Value;

    private const string Interceptor = "Kronikol.Extensions.Kafka.BuildInterception.KafkaBuildInterceptor";

    [Fact]
    public void Reset_says_the_patches_stay()
    {
        var summary = Summary("Kronikol.Extensions.Kafka.BuildInterception", $"M:{Interceptor}.Reset");

        Assert.DoesNotContain("removes", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stay for the life of the process", summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DisableConsumerTracking``2")]
    [InlineData("DisableProducerTracking``2")]
    public void Disabling_says_the_pair_gets_Confluents_own_client(string method)
    {
        var summary = Summary("Kronikol.Extensions.Kafka.BuildInterception", $"M:{Interceptor}.{method}");

        Assert.DoesNotContain("no-op", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Confluent's own", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_classifier_does_not_say_it_reads_HTTP_requests() =>
        Assert.DoesNotContain("HTTP", Summary("Kronikol.Extensions.Kafka", "T:Kronikol.Extensions.Kafka.KafkaOperationClassifier"), StringComparison.Ordinal);

    [Fact]
    public void The_accessor_option_says_a_message_carries_the_scenario_it_was_produced_in() =>
        Assert.Contains("whichever thread the delivery report arrives on",
            Summary("Kronikol.Extensions.Kafka", "P:Kronikol.Extensions.Kafka.KafkaTrackingOptions.HttpContextAccessor"), StringComparison.Ordinal);
}
