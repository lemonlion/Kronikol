using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Confluent.Kafka;
using HarmonyLib;
using Microsoft.AspNetCore.Http;

namespace Kronikol.Extensions.Kafka.BuildInterception;

/// <summary>
/// Intercepts <see cref="ConsumerBuilder{TKey,TValue}.Build()"/>, <see cref="ProducerBuilder{TKey,TValue}.Build()"/>
/// and <see cref="DependentProducerBuilder{TKey,TValue}.Build()"/> at runtime via Harmony, so a client built inside the code under test is wrapped with tracking when its key and value
/// types are enabled. This enables zero-production-code-change Kafka tracking in tests.
/// </summary>
/// <remarks>
/// <para>The runtime shares one body of a generic method among every type pair whose arguments agree on value types
/// and are otherwise reference types: <c>&lt;string,string&gt;</c>, <c>&lt;Null,string&gt;</c> and
/// <c>&lt;string,byte[]&gt;</c> share one, <c>&lt;int,string&gt;</c> and <c>&lt;int,byte[]&gt;</c> another. A patch
/// therefore reaches every pair of the enabled pair's shape, and it builds each builder's own client, Confluent's client
/// of that builder's key and value types, wrapping it only when that pair is enabled. A pair that is not enabled gets
/// exactly the client Confluent's <c>Build()</c> makes.</para>
/// <para>A patch stays for the life of the process. <see cref="Reset"/> and the <c>Disable…</c> methods clear tracking,
/// not patches: removing a patch from shared code cannot restore it.</para>
/// <para>Under tiered compilation the runtime can recompile <c>Build()</c> after its first few dozen calls, and the patch
/// is then lost for the rest of the process. Setting
/// <c>&lt;TieredCompilationQuickJit&gt;false&lt;/TieredCompilationQuickJit&gt;</c> in the test project keeps it;
/// without it, enabling says so on standard error and in the report's diagnostics.</para>
/// <para>Enable interception before the code that builds the clients first runs, in a fixture's constructor or before the
/// host starts. A method is compiled at its first call, and with quick JIT off it is compiled with optimizations, which
/// can inline <c>Build()</c> into it; one compiled before the patch keeps the inlined original, so its builds are
/// untracked. A test method that enables interception and then builds a client was compiled before its own
/// <c>Enable…</c> ran.</para>
/// </remarks>
public static class KafkaBuildInterceptor
{
    private const string HarmonyId = "Kronikol.Kafka.BuildInterception";

    private static readonly Harmony HarmonyInstance = new(HarmonyId);

    // The code shapes patched so far, by their representative (object for every reference type). Checked and patched
    // under one lock, so a host that enables while another is patching waits for the patch instead of building without it.
    private static readonly object PatchLock = new();
    private static readonly HashSet<Type> PatchedShapes = [];

    // What replaces Build() for each builder type met so far: Confluent's internal constructor, then the wrap.
    private static readonly ConcurrentDictionary<Type, Func<object, object>> Builds = new();

    // The builder kinds whose Build() is replaced, and the Confluent client each one builds.
    private static readonly Dictionary<Type, string> ClientOf = new()
    {
        [typeof(ProducerBuilder<,>)] = "Confluent.Kafka.Producer`2",
        [typeof(DependentProducerBuilder<,>)] = "Confluent.Kafka.Producer`2",
        [typeof(ConsumerBuilder<,>)] = "Confluent.Kafka.Consumer`2",
    };

    private static readonly MethodInfo BuildPrefixMethod =
        typeof(KafkaBuildInterceptor).GetMethod(nameof(BuildPrefix), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo WrapProducerDefinition =
        typeof(KafkaTrackingInterceptor).GetMethod(nameof(KafkaTrackingInterceptor.WrapProducer))!;

    private static readonly MethodInfo WrapConsumerDefinition =
        typeof(KafkaTrackingInterceptor).GetMethod(nameof(KafkaTrackingInterceptor.WrapConsumer))!;

    /// <summary>Finds Confluent's internal constructor of a client type taking its builder type, or null; replaced in tests.</summary>
    internal static Func<Type, Type, ConstructorInfo?> ConstructorLookup { get; set; } = FindConstructor;

    /// <summary>The tiered compilation check every <c>Enable…</c> runs; replaced in tests.</summary>
    internal static TieredCompilationCheck TieringCheck { get; set; } = TieredCompilationCheck.ForThisProcess();

    /// <summary>
    /// Enables consumer tracking and patches <see cref="ConsumerBuilder{TKey,TValue}.Build()"/>
    /// to automatically return a <see cref="TrackingKafkaConsumer{TKey,TValue}"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">The referenced Confluent.Kafka has no internal constructor to build a
    /// consumer from its builder; nothing is enabled or patched.</exception>
    public static void EnableConsumerTracking<TKey, TValue>(
        KafkaTrackingOptions options,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        RequireConstructor(typeof(ConsumerBuilder<TKey, TValue>));
        KafkaTrackingInterceptor.EnableConsumerTracking<TKey, TValue>(options, httpContextAccessor);
        EnsurePatched(typeof(ConsumerBuilder<,>), typeof(TKey), typeof(TValue));
        TieringCheck.ReportOnce();
    }

    /// <summary>
    /// Enables consumer tracking with an options configuration action and patches Build().
    /// </summary>
    public static void EnableConsumerTracking<TKey, TValue>(
        Action<KafkaTrackingOptions>? configure = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var options = new KafkaTrackingOptions();
        configure?.Invoke(options);
        EnableConsumerTracking<TKey, TValue>(options, httpContextAccessor);
    }

    /// <summary>
    /// Enables producer tracking and patches <see cref="ProducerBuilder{TKey,TValue}.Build()"/> and
    /// <see cref="DependentProducerBuilder{TKey,TValue}.Build()"/> to automatically return a
    /// <see cref="TrackingKafkaProducer{TKey,TValue}"/>. A dependent producer shares its owner's client, and its delivery
    /// reports arrive on the owner's poll thread; each produce is still recorded under the test that made it.
    /// </summary>
    /// <exception cref="NotSupportedException">The referenced Confluent.Kafka has no internal constructor to build a
    /// producer from its builder; nothing is enabled or patched.</exception>
    public static void EnableProducerTracking<TKey, TValue>(
        KafkaTrackingOptions options,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        RequireConstructor(typeof(ProducerBuilder<TKey, TValue>));
        RequireConstructor(typeof(DependentProducerBuilder<TKey, TValue>));
        KafkaTrackingInterceptor.EnableProducerTracking<TKey, TValue>(options, httpContextAccessor);
        EnsurePatched(typeof(ProducerBuilder<,>), typeof(TKey), typeof(TValue));
        EnsurePatched(typeof(DependentProducerBuilder<,>), typeof(TKey), typeof(TValue));
        TieringCheck.ReportOnce();
    }

    /// <summary>
    /// Enables producer tracking with an options configuration action and patches Build().
    /// </summary>
    public static void EnableProducerTracking<TKey, TValue>(
        Action<KafkaTrackingOptions>? configure = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var options = new KafkaTrackingOptions();
        configure?.Invoke(options);
        EnableProducerTracking<TKey, TValue>(options, httpContextAccessor);
    }

    /// <summary>
    /// Enables both consumer and producer tracking and patches both Build() methods.
    /// </summary>
    public static void EnableTracking<TKey, TValue>(
        KafkaTrackingOptions options,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        EnableConsumerTracking<TKey, TValue>(options, httpContextAccessor);
        EnableProducerTracking<TKey, TValue>(options, httpContextAccessor);
    }

    /// <summary>
    /// Enables both consumer and producer tracking with an options configuration action.
    /// </summary>
    public static void EnableTracking<TKey, TValue>(
        Action<KafkaTrackingOptions>? configure = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var options = new KafkaTrackingOptions();
        configure?.Invoke(options);
        EnableTracking<TKey, TValue>(options, httpContextAccessor);
    }

    /// <summary>Disables consumer tracking for this pair. The patch stays, and this pair's builders return Confluent's own consumer.</summary>
    public static void DisableConsumerTracking<TKey, TValue>()
    {
        KafkaTrackingInterceptor.DisableConsumerTracking<TKey, TValue>();
    }

    /// <summary>Disables producer tracking for this pair. The patch stays, and this pair's builders return Confluent's own producer.</summary>
    public static void DisableProducerTracking<TKey, TValue>()
    {
        KafkaTrackingInterceptor.DisableProducerTracking<TKey, TValue>();
    }

    /// <summary>
    /// Clears all tracking state. The Build() patches stay for the life of the process, so every builder returns
    /// Confluent's own client until its pair is enabled again.
    /// </summary>
    public static void Reset()
    {
        KafkaTrackingInterceptor.Reset();
    }

    // Q3: without the internal constructor there is nothing to build a client with, and patching would leave every
    // builder of the shape broken, so refuse before anything is enabled or patched.
    private static void RequireConstructor(Type builder)
    {
        if (ConstructorFor(builder) is not null)
            return;

        var client = ClientOf[builder.GetGenericTypeDefinition()];
        throw new NotSupportedException(
            $"Kafka build interception builds a {client.Replace("`2", "")} through Confluent.Kafka's internal constructor that takes " +
            $"a {builder.Name.Replace("`2", "")}, and Confluent.Kafka {typeof(IProducer<,>).Assembly.GetName().Version} has none, " +
            "so nothing was enabled or patched. Wrap the client with .Tracked() or build it with .BuildTracked(), or register " +
            "it with the AddKafka…TestTracking extensions, instead.");
    }

    private static ConstructorInfo? ConstructorFor(Type closedBuilder)
    {
        var client = typeof(IProducer<,>).Assembly.GetType(ClientOf[closedBuilder.GetGenericTypeDefinition()]);
        return client is null
            ? null
            : ConstructorLookup(client.MakeGenericType(closedBuilder.GetGenericArguments()), closedBuilder);
    }

    private static ConstructorInfo? FindConstructor(Type client, Type builder) =>
        client.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [builder], null);

    private static void EnsurePatched(Type builderDefinition, Type key, Type value)
    {
        var representative = builderDefinition.MakeGenericType(Shape(key), Shape(value));
        lock (PatchLock)
        {
            if (PatchedShapes.Contains(representative))
                return;

            HarmonyInstance.Patch(representative.GetMethod("Build", Type.EmptyTypes)!, prefix: new HarmonyMethod(BuildPrefixMethod));
            PatchedShapes.Add(representative);
        }
    }

    // A type argument's code shape: every reference type shares one body (object stands for it), a value type has its
    // own, and a generic value type is shaped by its own arguments (KeyValuePair<Uri,Uri> shares KeyValuePair<string,string>'s).
    private static Type Shape(Type type) =>
        !type.IsValueType ? typeof(object)
        : type.IsGenericType ? type.GetGenericTypeDefinition().MakeGenericType(type.GetGenericArguments().Select(Shape).ToArray())
        : type;

    // Runs in place of Build() for every builder of a patched shape. The original body was compiled from one pair's IL
    // and is every other pair's of the shape, so it must never run.
    private static bool BuildPrefix(object __instance, ref object __result)
    {
        __result = Builds.GetOrAdd(__instance.GetType(), CompileBuild)(__instance);
        return false;
    }

    // new Producer<K,V>(builder) or new Consumer<K,V>(builder), what Confluent's Build() does, then the wrap, compiled
    // once per builder type: a call through reflection would wrap Confluent's refusals in TargetInvocationException. The
    // pair comes from the nearest generic base type, so a builder subclass is built as its base pair.
    private static Func<object, object> CompileBuild(Type builderType)
    {
        var closedBuilder = builderType;
        while (closedBuilder is not null && !(closedBuilder.IsGenericType && ClientOf.ContainsKey(closedBuilder.GetGenericTypeDefinition())))
            closedBuilder = closedBuilder.BaseType;
        if (closedBuilder is null)
            throw new InvalidOperationException($"{builderType} is not a Confluent.Kafka producer or consumer builder.");

        var constructor = ConstructorFor(closedBuilder)
            ?? throw new NotSupportedException($"Confluent.Kafka has no internal constructor taking a {closedBuilder}.");
        var wrap = (closedBuilder.GetGenericTypeDefinition() == typeof(ConsumerBuilder<,>) ? WrapConsumerDefinition : WrapProducerDefinition)
            .MakeGenericMethod(closedBuilder.GetGenericArguments());

        var builder = Expression.Parameter(typeof(object), "builder");
        var body = Expression.Call(wrap, Expression.New(constructor, Expression.Convert(builder, closedBuilder)));
        return Expression.Lambda<Func<object, object>>(body, builder).Compile();
    }
}
