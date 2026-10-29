using DotNet.Testcontainers.Builders;
using Testcontainers.Kafka;

namespace Kronikol.Tests.Kafka;

/// <summary>
/// A real Kafka broker, for what only Confluent's clients against a broker do (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md
/// 5.4): delivery reports from the broker, a dependent producer sharing its owner's connection, a consumer group. The
/// broker <see cref="EnvironmentVariable"/> names when it is set, otherwise one Testcontainers starts when a Docker
/// endpoint answers (CI's Linux runners). Without either the facts that need it skip, saying why.
/// </summary>
public sealed class KafkaBrokerFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "KRONIKOL_TEST_KAFKA";

    // The image Kronikol4J's broker facts run.
    public const string Image = "confluentinc/cp-kafka:7.6.1";

    private KafkaContainer? _container;

    public string? BootstrapServers { get; private set; }
    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            BootstrapServers = configured;
            return;
        }

        // Testcontainers says when no Docker endpoint answers; any other failure to start the broker fails the facts.
        try
        {
            _container = new KafkaBuilder(Image).Build();
            await _container.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            UnavailableReason = $"Docker is not available ({ex.Message.Split('\n')[0].Trim()}) and {EnvironmentVariable} names no Kafka broker.";
            _container = null;
            return;
        }
        BootstrapServers = _container.GetBootstrapAddress();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>The broker's bootstrap address; skips the calling fact when there is no broker.</summary>
    public string Require()
    {
        Assert.SkipWhen(BootstrapServers is null, UnavailableReason ?? "");
        return BootstrapServers!;
    }
}
