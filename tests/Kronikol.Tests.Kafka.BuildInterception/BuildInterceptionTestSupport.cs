using System.Text;
using Confluent.Kafka;
using HarmonyLib;

namespace Kronikol.Tests.Kafka.BuildInterception;

internal static class BuildInterceptionTestSupport
{
    /// <summary>The Harmony id the interceptor patches under.</summary>
    public const string HarmonyId = "Kronikol.Kafka.BuildInterception";

    // No broker runs: a produce is queued and times out after a second, so disposing a producer never waits long.
    public static ProducerConfig ProducerConfig() => new() { BootstrapServers = "localhost:9092", MessageTimeoutMs = 1000 };

    public static ConsumerConfig ConsumerConfig() => new() { GroupId = "build-interception", BootstrapServers = "localhost:9092" };

    /// <summary>Asserts <paramref name="client"/> is Confluent's own client (<c>Producer`2</c> or <c>Consumer`2</c>) of exactly this pair.</summary>
    public static void AssertConfluentClient(object client, string definition, Type key, Type value)
    {
        var type = client.GetType();
        Assert.True(type.IsGenericType, $"{type} is not Confluent's generic {definition}");
        Assert.Equal($"Confluent.Kafka.{definition}", type.GetGenericTypeDefinition().FullName);
        Assert.Equal(new[] { key, value }, type.GetGenericArguments());
    }

    /// <summary>How many prefixes the interceptor installed on <paramref name="builderType"/>'s <c>Build()</c>, the shape's code.</summary>
    public static int InterceptorPrefixes(Type builderType)
    {
        var info = Harmony.GetPatchInfo(builderType.GetMethod("Build", Type.EmptyTypes)!);
        return info?.Prefixes.Count(p => p.owner == HarmonyId) ?? 0;
    }
}

/// <summary>Serialises any key or value as its text, for pairs Confluent has no serializer for.</summary>
internal sealed class ToStringSerializer<T> : ISerializer<T>
{
    public byte[] Serialize(T data, SerializationContext context) => Encoding.UTF8.GetBytes(data?.ToString() ?? "");
}

// Value types no other fact uses, so each race or refusal fact meets a code shape nothing has patched yet.
internal readonly record struct RaceKeyA(int Value);

internal readonly record struct RaceKeyB(int Value);

internal readonly record struct MissingConstructorKey(int Value);
