// audit B experiments (not repo code) for plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md: E1-E4, E6-E8, and the
// harness prototype verbatim ("proto"). Published packages: Kronikol.Extensions.Kafka(.BuildInterception) 4.14.6,
// Confluent.Kafka 2.16.0 (the 4.14.6 nuspec's minimum), Lib.Harmony 2.4.2 or 2.4.3 (-p:HarmonyVersion).
// Every mode runs in its own process: a Harmony patch on shared generic code cannot be undone.
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using Confluent.Kafka;
using HarmonyLib;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.Kafka.BuildInterception;
using Kronikol.Tracking;

var mode = args.Length > 0 ? args[0] : "proto";
Console.WriteLine($"== {mode}  runtime={RuntimeInformation.FrameworkDescription}  Kronikol.Extensions.Kafka={U.Ver(typeof(KafkaTrackingInterceptor))}  " +
                  $"Confluent.Kafka={U.Ver(typeof(ProducerBuilder<,>))}  Lib.Harmony={U.Ver(typeof(Harmony))}");
switch (mode)
{
    case "proto": E.Proto(); break;
    case "e1": E.E1(); break;
    case "e2a-int": E.E2aInt(); break;
    case "e2a-str": E.E2aStr(); break;
    case "e2b": E.E2b(); break;
    case "e2c": E.E2c(); break;
    case "e3-stack": E.E3Stack(); break;
    case "e3-dup": E.E3Dup(); break;
    case "tierup-proto": await E.TierupProto(args.Length > 1 ? args[1] : "proto-helper"); break;
    case "e3-race": E.E3Race(args.Length > 1 ? args[1] : "tryadd"); break;
    case "e4": await E.E4(); break;
    case "e6": await E.E6(); break;
    case "e7": E.E7(args.Skip(1).ToArray()); break;
    case "e8a": await E.E8a(); break;
    case "e8b": await E.E8b(); break;
    case "e8c": await E.E8c(); break;
    default: Console.WriteLine("unknown mode"); break;
}
Console.WriteLine($"== done (live librdkafka handles: {Library.HandleCount})");

static class U
{
    public static string Ver(Type t) =>
        t.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "?";

    public static string N(object? o) => o is null ? "null" : N(o.GetType());

    public static string N(Type t) =>
        t.IsGenericType ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(N))}>" : t.Name;

    public static ProducerConfig PCfg(bool deliveryReports = true, params (string K, string V)[] extra)
    {
        var c = new ProducerConfig { BootstrapServers = "localhost:1", MessageTimeoutMs = 1000, EnableDeliveryReports = deliveryReports };
        foreach (var (k, v) in extra) c.Set(k, v);
        return c;
    }

    public static ConsumerConfig CCfg() => new() { BootstrapServers = "localhost:1", GroupId = "audit-b" };

    public static IProducer<K, V> Build<K, V>(ProducerConfig? cfg = null, ISerializer<K>? keySerializer = null)
    {
        var b = new ProducerBuilder<K, V>(cfg ?? PCfg()).SetLogHandler((_, _) => { });
        if (keySerializer is not null) b.SetKeySerializer(keySerializer);
        return b.Build();
    }

    public static string Ex(Exception e) =>
        $"{e.GetType().FullName}{(e.InnerException is null ? "" : $" [inner {e.InnerException.GetType().FullName}]")}: {e.Message.Split('\n')[0].Trim()}";

    public static void Opt(KafkaTrackingOptions o) => o.CurrentTestInfoFetcher = () => ("audit-b", "audit-b");

    // Confluent's Producer<,> is internal: compare by name and type arguments.
    public static bool IsConfluentProducer<K, V>(object o) =>
        o.GetType().FullName!.StartsWith("Confluent.Kafka.Producer`2") &&
        o.GetType().GetGenericArguments().SequenceEqual([typeof(K), typeof(V)]);

    public static string CallerOf<K, V>(TrackingKafkaProducer<K, V> p) =>
        ((KafkaTrackingOptions)typeof(TrackingKafkaProducer<K, V>).GetField("_options", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(p)!).CallerName;
}

sealed class ToStringSerializer<T> : ISerializer<T>
{
    public byte[] Serialize(T data, SerializationContext context) => Encoding.UTF8.GetBytes(data?.ToString() ?? "");
}

sealed class SubBuilder(IEnumerable<KeyValuePair<string, string>> config) : ProducerBuilder<string, string>(config);

/// <summary>The harness prototype's dispatching prefix, with two exception-preserving variants (E1).</summary>
static class Proto
{
    public static int PrefixCalls;
    public static int Strategy; // 0 = prototype verbatim, 1 = BindingFlags.DoNotWrapExceptions, 2 = compiled delegate
    public static readonly Harmony H = new("audit.b.proto");
    static readonly Type ProducerOpen = typeof(IProducer<,>).Assembly.GetType("Confluent.Kafka.Producer`2", throwOnError: true)!;
    static readonly MethodInfo WrapOpen = typeof(KafkaTrackingInterceptor).GetMethod(nameof(KafkaTrackingInterceptor.WrapProducer))!;
    public static readonly MethodInfo PrefixMethod = typeof(Proto).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)!;
    static readonly ConcurrentDictionary<Type, Func<object, object>> Compiled = new();

    public static MethodInfo Patch(Type closedBuilder, Harmony? harmony = null) =>
        (harmony ?? H).Patch(closedBuilder.GetMethod("Build", Type.EmptyTypes)!, prefix: new HarmonyMethod(PrefixMethod));

    static bool Prefix(object __instance, ref object __result)
    {
        Interlocked.Increment(ref PrefixCalls);
        var args = __instance.GetType().GetGenericArguments();
        switch (Volatile.Read(ref Strategy))
        {
            case 1:
            {
                var producer = Activator.CreateInstance(ProducerOpen.MakeGenericType(args),
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DoNotWrapExceptions,
                    null, [__instance], null)!;
                __result = WrapOpen.MakeGenericMethod(args).Invoke(null, BindingFlags.DoNotWrapExceptions, null, [producer], null)!;
                break;
            }
            case 2:
                __result = Compiled.GetOrAdd(__instance.GetType(), Compile)(__instance);
                break;
            default:
            {
                // verbatim from the harness prototype/Program.cs
                var producer = Activator.CreateInstance(ProducerOpen.MakeGenericType(args),
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, [__instance], null)!;
                __result = WrapOpen.MakeGenericMethod(args).Invoke(null, [producer])!;
                break;
            }
        }
        return false;
    }

    static Func<object, object> Compile(Type builderType)
    {
        var args = builderType.GetGenericArguments();
        var builderClosed = typeof(ProducerBuilder<,>).MakeGenericType(args);
        var ctor = ProducerOpen.MakeGenericType(args).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [builderClosed], null)
                   ?? throw new NotSupportedException("internal Producer(ProducerBuilder<K,V>) not found");
        var p = Expression.Parameter(typeof(object), "builder");
        var call = Expression.Call(WrapOpen.MakeGenericMethod(args), Expression.New(ctor, Expression.Convert(p, builderClosed)));
        return Expression.Lambda<Func<object, object>>(call, p).Compile();
    }
}

static class E
{
    // ───────────────────────── proto: the harness prototype, verbatim checks ─────────────────────────
    public static void Proto()
    {
        static string Name(object o) => $"{o.GetType().Name}<{string.Join(",", o.GetType().GetGenericArguments().Select(a => a.Name))}>";
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(o => { o.CurrentTestInfoFetcher = () => ("probe", "probe-id"); });
        global::Proto.Patch(typeof(ProducerBuilder<string, string>));
        global::Proto.Patch(typeof(DependentProducerBuilder<string, string>));
        var cfg = new ProducerConfig { BootstrapServers = "localhost:9092", MessageTimeoutMs = 1000 };
        using var a = new ProducerBuilder<string, string>(cfg).Build();
        Console.WriteLine($"ProducerBuilder<string,string>          -> {Name(a)}  ok={((object)a) is IProducer<string, string>}");
        using var n = new ProducerBuilder<Null, string>(cfg).Build();
        Console.WriteLine($"ProducerBuilder<Null,string>            -> {Name(n)}  ok={((object)n) is IProducer<Null, string>}");
        using var b = new ProducerBuilder<string, byte[]>(cfg).Build();
        Console.WriteLine($"ProducerBuilder<string,byte[]>          -> {Name(b)}  ok={((object)b) is IProducer<string, byte[]>}");
        using var d = new DependentProducerBuilder<string, string>(a.Handle).Build();
        Console.WriteLine($"DependentProducerBuilder<string,string> -> {Name(d)}  ok={((object)d) is IProducer<string, string>}");
        using var dn = new DependentProducerBuilder<Null, string>(a.Handle).Build();
        Console.WriteLine($"DependentProducerBuilder<Null,string>   -> {Name(dn)}  ok={((object)dn) is IProducer<Null, string>}");
        try { n.Produce("t135", new Message<Null, string> { Value = "v" }); Console.WriteLine("  Produce on <Null,string>: no exception (queued; no broker)"); }
        catch (Exception ex) { Console.WriteLine($"  Produce on <Null,string> threw {ex.GetType().Name}"); }
    }

    // ───────────────────────── tier-up of the dispatching prefix itself (F2 applies to the proposed design) ─────────────────────────
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static IProducer<string, string> BuildDirect() =>
        new ProducerBuilder<string, string>(U.PCfg()).SetLogHandler((_, _) => { }).Build();

    public static async Task TierupProto(string variant)
    {
        if (variant.StartsWith("shipped")) KafkaBuildInterceptor.EnableProducerTracking<string, string>(U.Opt);
        else
        {
            KafkaTrackingInterceptor.EnableProducerTracking<string, string>(U.Opt);
            if (variant.StartsWith("proto2")) global::Proto.Strategy = 2; // compiled delegate, no reflection per build
            global::Proto.Patch(typeof(ProducerBuilder<string, string>));
        }
        var strategy = global::Proto.Strategy;
        Console.WriteLine($"  variant={variant} (interception: {(variant.StartsWith("shipped") ? "shipped KafkaBuildInterceptor postfix" : "dispatching prefix, strategy " + strategy)}; " +
                          $"call site: {(variant.Contains("direct") ? "non-generic NoInlining method" : "generic helper U.Build<K,V>")})");
        int tracked = 0, firstUntracked = -1;
        for (var i = 0; i < 300; i++)
        {
            using var p = variant.Contains("direct") ? BuildDirect() : U.Build<string, string>();
            if (p is TrackingKafkaProducer<string, string>) tracked++;
            else if (firstUntracked < 0) firstUntracked = i;
            if (i % 50 == 0) await Task.Delay(50);
            if (variant.Contains("pause") && i % 10 == 9) await Task.Delay(200); // a suite has quiet periods between tests
        }
        Console.WriteLine($"  300 x ProducerBuilder<string,string>.Build() under the prototype prefix: tracked={tracked} firstUntrackedAt={firstUntracked} " +
                          $"(env DOTNET_TC_QuickJit={Environment.GetEnvironmentVariable("DOTNET_TC_QuickJit") ?? "unset"})");
    }

    // ───────────────────────── E1: exception wrapping ─────────────────────────
    public static void E1()
    {
        var cases = new (string Name, Func<object> Build)[]
        {
            ("unknown property no.such.property=x", () => U.Build<string, string>(U.PCfg(true, ("no.such.property", "x")))),
            ("invalid value acks=banana", () => U.Build<string, string>(U.PCfg(true, ("acks", "banana")))),
            ("delivery.report.only.error=true", () => U.Build<string, string>(U.PCfg(true, ("delivery.report.only.error", "true")))),
            ("dotnet.producer.enable.delivery.reports=maybe", () => U.Build<string, string>(U.PCfg(true, ("dotnet.producer.enable.delivery.reports", "maybe")))),
            ("<Uri,string> with no key serializer", () => U.Build<Uri, string>()),
        };

        void RunAll(string label)
        {
            Console.WriteLine($"-- {label}");
            foreach (var (name, build) in cases)
            {
                try
                {
                    var p = build();
                    Console.WriteLine($"  {name,-46} -> built {U.N(p)} (no exception)");
                    (p as IDisposable)?.Dispose();
                }
                catch (Exception e) { Console.WriteLine($"  {name,-46} -> {U.Ex(e)}"); }
            }
        }

        RunAll("(a) unpatched: Confluent's own Build()");
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(U.Opt);
        global::Proto.Patch(typeof(ProducerBuilder<string, string>));
        global::Proto.Strategy = 0;
        RunAll("(b) prototype prefix patched (Activator.CreateInstance + MethodInfo.Invoke)");
        global::Proto.Strategy = 1;
        RunAll("(c) fix 1: BindingFlags.DoNotWrapExceptions on CreateInstance and Invoke");
        global::Proto.Strategy = 2;
        RunAll("(d) fix 2: cached compiled delegate (Expression.New on the internal ctor, then WrapProducer)");
        Console.WriteLine($"  prefix calls={(global::Proto.PrefixCalls)}");
    }

    // ───────────────────────── E2: partial generic sharing ─────────────────────────
    static void TryP<K, V>(Message<K, V> m, ISerializer<K>? keySerializer = null)
    {
        var label = $"ProducerBuilder<{U.N(typeof(K))},{U.N(typeof(V))}>";
        try
        {
            var before = global::Proto.PrefixCalls;
            var p = U.Build<K, V>(keySerializer: keySerializer);
            var ran = global::Proto.PrefixCalls - before;
            object o = p;
            Console.WriteLine($"  {label,-46}.Build() -> {U.N(o),-46} is IProducer<{U.N(typeof(K))},{U.N(typeof(V))}>={o is IProducer<K, V>}  " +
                              $"tracked={o is TrackingKafkaProducer<K, V>}  prototypePrefixRan={ran}");
            try { p.Produce("audit-topic", m); Console.WriteLine("        Produce -> ok"); }
            catch (Exception e) { Console.WriteLine($"        Produce -> {U.Ex(e)}"); }
            try { p.Dispose(); } catch (Exception e) { Console.WriteLine($"        Dispose -> {U.Ex(e)}"); }
        }
        catch (Exception e) { Console.WriteLine($"  {label}: {U.Ex(e)}"); }
    }

    static void TryC<K, V>()
    {
        var label = $"ConsumerBuilder<{U.N(typeof(K))},{U.N(typeof(V))}>";
        try
        {
            var c = new ConsumerBuilder<K, V>(U.CCfg()).SetLogHandler((_, _) => { }).Build();
            object o = c;
            Console.WriteLine($"  {label,-46}.Build() -> {U.N(o),-46} is IConsumer<{U.N(typeof(K))},{U.N(typeof(V))}>={o is IConsumer<K, V>}");
            try { c.Subscribe("audit-topic"); Console.WriteLine("        Subscribe -> ok"); }
            catch (Exception e) { Console.WriteLine($"        Subscribe -> {U.Ex(e)}"); }
            try { c.Dispose(); } catch (Exception e) { Console.WriteLine($"        Dispose -> {U.Ex(e)}"); }
        }
        catch (Exception e) { Console.WriteLine($"  {label}: {U.Ex(e)}"); }
    }

    public static void E2aInt()
    {
        Console.WriteLine("-- SHIPPED KafkaBuildInterceptor: EnableProducerTracking<int,string>() and EnableConsumerTracking<int,string>() only");
        KafkaBuildInterceptor.EnableProducerTracking<int, string>(U.Opt);
        KafkaBuildInterceptor.EnableConsumerTracking<int, string>(U.Opt);
        TryP(new Message<int, string> { Key = 1, Value = "v" });
        TryP(new Message<int, byte[]> { Key = 1, Value = [1] });
        TryP(new Message<int, Null> { Key = 1, Value = null! });
        TryP(new Message<long, string> { Key = 1, Value = "v" });
        TryP(new Message<string, string> { Key = "k", Value = "v" });
        TryC<int, string>();
        TryC<int, byte[]>();
        TryC<long, string>();
    }

    public static void E2aStr()
    {
        Console.WriteLine("-- SHIPPED KafkaBuildInterceptor: EnableProducerTracking<string,int>() only");
        KafkaBuildInterceptor.EnableProducerTracking<string, int>(U.Opt);
        TryP(new Message<string, int> { Key = "k", Value = 1 });
        TryP(new Message<Null, int> { Value = 1 });
        TryP(new Message<byte[], int> { Key = [1], Value = 1 });
        TryP(new Message<string, long> { Key = "k", Value = 1 });
        TryP(new Message<string, string> { Key = "k", Value = "v" });
    }

    public static void E2b()
    {
        Console.WriteLine("-- prototype prefix patched on ProducerBuilder<int,string>.Build ONLY; tracking enabled for <int,string>, <int,byte[]>, <long,string>");
        KafkaTrackingInterceptor.EnableProducerTracking<int, string>(U.Opt);
        KafkaTrackingInterceptor.EnableProducerTracking<int, byte[]>(U.Opt);
        KafkaTrackingInterceptor.EnableProducerTracking<long, string>(U.Opt);
        global::Proto.Patch(typeof(ProducerBuilder<int, string>));
        TryP(new Message<int, string> { Key = 1, Value = "v" });
        TryP(new Message<int, byte[]> { Key = 1, Value = [1] });
        TryP(new Message<long, string> { Key = 1, Value = "v" });
        TryP(new Message<string, string> { Key = "k", Value = "v" });
        Console.WriteLine("-- generic struct key: prototype prefix patched on ProducerBuilder<KeyValuePair<string,string>,string>.Build; " +
                          "tracking enabled for <KeyValuePair<Uri,Uri>,string> and <KeyValuePair<string,int>,string>, neither patched");
        KafkaTrackingInterceptor.EnableProducerTracking<KeyValuePair<Uri, Uri>, string>(U.Opt);
        KafkaTrackingInterceptor.EnableProducerTracking<KeyValuePair<string, int>, string>(U.Opt);
        global::Proto.Patch(typeof(ProducerBuilder<KeyValuePair<string, string>, string>));
        TryP(new Message<KeyValuePair<Uri, Uri>, string> { Key = new(new Uri("http://a"), new Uri("http://b")), Value = "v" },
            new ToStringSerializer<KeyValuePair<Uri, Uri>>());
        TryP(new Message<KeyValuePair<string, int>, string> { Key = new("a", 1), Value = "v" },
            new ToStringSerializer<KeyValuePair<string, int>>());
        Console.WriteLine("-- a builder SUBCLASS (class SubBuilder : ProducerBuilder<string,string>), after patching <string,string>: the prototype reads __instance.GetType().GetGenericArguments()");
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(U.Opt);
        global::Proto.Patch(typeof(ProducerBuilder<string, string>));
        try
        {
            var p = new SubBuilder(U.PCfg()).SetLogHandler((_, _) => { }).Build();
            Console.WriteLine($"  SubBuilder.Build() -> {U.N(p)}");
            p.Dispose();
        }
        catch (Exception e) { Console.WriteLine($"  SubBuilder.Build() -> {U.Ex(e)}"); }
    }

    // A shape key: every reference type becomes object, recursively through generic value types.
    static Type Canon(Type t) =>
        !t.IsValueType ? typeof(object)
        : t.IsGenericType ? t.GetGenericTypeDefinition().MakeGenericType(t.GetGenericArguments().Select(Canon).ToArray())
        : t;

    public static void E2c()
    {
        Console.WriteLine("-- patch through the shape's representative: ProducerBuilder<Canon(K),Canon(V)>.Build, where Canon maps every reference type to object");
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(U.Opt);
        KafkaTrackingInterceptor.EnableProducerTracking<int, byte[]>(U.Opt);
        KafkaTrackingInterceptor.EnableProducerTracking<KeyValuePair<Uri, Uri>, string>(U.Opt);
        foreach (var pair in new[] { (typeof(string), typeof(string)), (typeof(int), typeof(byte[])), (typeof(KeyValuePair<Uri, Uri>), typeof(string)), (typeof(Null), typeof(string)), (typeof(int), typeof(string)) })
        {
            var rep = typeof(ProducerBuilder<,>).MakeGenericType(Canon(pair.Item1), Canon(pair.Item2));
            var already = Harmony.GetPatchInfo(rep.GetMethod("Build", Type.EmptyTypes)!)?.Prefixes.Any(p => p.owner == global::Proto.H.Id) == true;
            if (!already) global::Proto.Patch(rep);
            Console.WriteLine($"  enable <{U.N(pair.Item1)},{U.N(pair.Item2)}> -> representative {U.N(rep)}: {(already ? "already patched, skipped" : "patched")}");
        }
        TryP(new Message<string, string> { Key = "k", Value = "v" });
        TryP(new Message<Null, string> { Value = "v" });
        TryP(new Message<int, byte[]> { Key = 1, Value = [1] });
        TryP(new Message<int, string> { Key = 1, Value = "v" });
        TryP(new Message<KeyValuePair<Uri, Uri>, string> { Key = new(new Uri("http://a"), new Uri("http://b")), Value = "v" }, new ToStringSerializer<KeyValuePair<Uri, Uri>>());
        TryP(new Message<long, string> { Key = 1, Value = "v" });
        Console.WriteLine($"  GetPatchInfo <object,object>: {Prefixes(typeof(ProducerBuilder<object, object>))}; <int,object>: {Prefixes(typeof(ProducerBuilder<int, object>))}");
    }

    // ───────────────────────── E3: stacking and concurrency ─────────────────────────
    static void M<K, V>()
    {
        var before = global::Proto.PrefixCalls;
        var h0 = Library.HandleCount;
        try
        {
            var p = U.Build<K, V>();
            var ran = global::Proto.PrefixCalls - before;
            var name = U.N(p);
            p.Dispose();
            Console.WriteLine($"    build <{U.N(typeof(K))},{U.N(typeof(V))}> -> {name,-46} prefix ran {ran}x; librdkafka handles left after Dispose: {Library.HandleCount - h0}");
        }
        catch (Exception e) { Console.WriteLine($"    build <{U.N(typeof(K))},{U.N(typeof(V))}> -> {U.Ex(e)}"); }
    }

    static void Measure(string label)
    {
        Console.WriteLine($"  {label}");
        M<string, string>();
        M<Null, string>();
        M<string, byte[]>();
    }

    static string Prefixes(Type closedBuilder)
    {
        var info = Harmony.GetPatchInfo(closedBuilder.GetMethod("Build", Type.EmptyTypes)!);
        return info is null ? "none" : $"{info.Prefixes.Count} prefix(es) [{string.Join(", ", info.Prefixes.Select(p => $"{p.owner}:{p.PatchMethod.Name}"))}]";
    }

    public static void E3Stack()
    {
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(U.Opt);
        Measure("(0) no patch");
        global::Proto.Patch(typeof(ProducerBuilder<string, string>));
        Measure($"(1) patched once via ProducerBuilder<string,string>.Build; GetPatchInfo: {Prefixes(typeof(ProducerBuilder<string, string>))}");
        global::Proto.Patch(typeof(ProducerBuilder<string, string>));
        Measure($"(2) the same MethodInfo patched again (same Harmony id, same prefix); GetPatchInfo: {Prefixes(typeof(ProducerBuilder<string, string>))}");
        global::Proto.Patch(typeof(ProducerBuilder<Null, string>));
        Measure($"(3) plus ProducerBuilder<Null,string>.Build (another closed MethodInfo, same shared code); GetPatchInfo <string,string>: " +
                $"{Prefixes(typeof(ProducerBuilder<string, string>))}; <Null,string>: {Prefixes(typeof(ProducerBuilder<Null, string>))}");
        global::Proto.Patch(typeof(ProducerBuilder<string, string>), new Harmony("audit.b.other-id"));
        Measure($"(4) plus a second Harmony id patching <string,string>.Build; GetPatchInfo: {Prefixes(typeof(ProducerBuilder<string, string>))}");
    }

    // Is a duplicate Patch call de-duplicated, or skipped only because the first prefix returned false?
    public static int PassThroughCalls;
    static void PassThroughPrefix() => Interlocked.Increment(ref PassThroughCalls);

    public static void E3Dup()
    {
        var build = typeof(ProducerBuilder<string, string>).GetMethod("Build", Type.EmptyTypes)!;
        var passThrough = new HarmonyMethod(typeof(E).GetMethod(nameof(PassThroughPrefix), BindingFlags.NonPublic | BindingFlags.Static)!);
        var h = new Harmony("audit.b.dup");
        h.Patch(build, prefix: passThrough);
        var c0 = PassThroughCalls; U.Build<string, string>().Dispose();
        Console.WriteLine($"  a void pass-through prefix patched once: ran {PassThroughCalls - c0}x per Build()");
        h.Patch(build, prefix: passThrough);
        c0 = PassThroughCalls; U.Build<string, string>().Dispose();
        Console.WriteLine($"  the same prefix patched a second time: ran {PassThroughCalls - c0}x per Build(); GetPatchInfo: {Prefixes(typeof(ProducerBuilder<string, string>))}");
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(U.Opt);
        global::Proto.Patch(typeof(ProducerBuilder<string, string>));
        global::Proto.Patch(typeof(ProducerBuilder<string, string>));
        c0 = PassThroughCalls; var before = global::Proto.PrefixCalls; var h0 = Library.HandleCount;
        var p = U.Build<string, string>(); p.Dispose();
        Console.WriteLine($"  plus the dispatching prefix patched twice: pass-through ran {PassThroughCalls - c0}x, dispatching prefix ran {(global::Proto.PrefixCalls - before)}x; " +
                          $"handles left {Library.HandleCount - h0}; result {U.N(p)}; GetPatchInfo: {Prefixes(typeof(ProducerBuilder<string, string>))}");
    }

    static readonly ConcurrentDictionary<string, bool> Patched = new();
    static readonly object Gate = new();

    static void EnableAndPatch(string variant, int host)
    {
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(o =>
        {
            o.CallerName = $"host{host}";
            o.CurrentTestInfoFetcher = () => ("audit-b", "audit-b");
        });
        const string shape = "__Canon,__Canon";
        switch (variant)
        {
            case "shipped": // the published KafkaBuildInterceptor 4.14.6 itself (TryAdd, then its closed postfix)
                KafkaBuildInterceptor.EnableProducerTracking<string, string>(o =>
                {
                    o.CallerName = $"host{host}";
                    o.CurrentTestInfoFetcher = () => ("audit-b", "audit-b");
                });
                break;
            case "unguarded": // the harness prototype's path: Patch on every enable
                global::Proto.Patch(typeof(ProducerBuilder<string, string>));
                break;
            case "tryadd": // the shipped KafkaBuildInterceptor's guard: TryAdd, then patch
                if (Patched.TryAdd(shape, true)) global::Proto.Patch(typeof(ProducerBuilder<string, string>));
                break;
            case "checkthenpatch": // mark after patching
                if (!Patched.ContainsKey(shape)) { global::Proto.Patch(typeof(ProducerBuilder<string, string>)); Patched[shape] = true; }
                break;
            case "lock":
                lock (Gate)
                {
                    if (!Patched.ContainsKey(shape)) { global::Proto.Patch(typeof(ProducerBuilder<string, string>)); Patched[shape] = true; }
                }
                break;
            default: throw new ArgumentException(variant);
        }
    }

    public static void E3Race(string variant)
    {
        const int Hosts = 10;
        Console.WriteLine($"-- variant={variant}: {Hosts} threads run enable+patch at once, each then builds its producer at once; 4 threads build <Null,string> and <string,string> throughout");
        var barrier = new Barrier(Hosts);
        var stop = 0;
        var bgBuilds = 0;
        var bgAnomalies = new ConcurrentBag<string>();
        var background = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                try
                {
                    var a = U.Build<Null, string>();
                    if (!U.IsConfluentProducer<Null, string>(a)) bgAnomalies.Add($"<Null,string> -> {U.N(a)}");
                    a.Dispose();
                    var s = U.Build<string, string>();
                    if (s is not IProducer<string, string>) bgAnomalies.Add($"<string,string> -> {U.N(s)}");
                    s.Dispose();
                    Interlocked.Add(ref bgBuilds, 2);
                }
                catch (Exception e) { bgAnomalies.Add(U.Ex(e)); }
            }
        })).ToList();
        background.ForEach(t => t.Start());
        Thread.Sleep(200);
        var results = new string[Hosts];
        var threads = Enumerable.Range(0, Hosts).Select(i => new Thread(() =>
        {
            barrier.SignalAndWait();
            try
            {
                EnableAndPatch(variant, i);
                var before = global::Proto.PrefixCalls;
                var p = U.Build<string, string>();
                results[i] = p is TrackingKafkaProducer<string, string> t
                    ? $"tracked, options of {U.CallerOf(t)}"
                    : $"UNTRACKED: {U.N(p)}";
                p.Dispose();
            }
            catch (Exception e) { results[i] = U.Ex(e); }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        Volatile.Write(ref stop, 1);
        background.ForEach(t => t.Join());
        for (var i = 0; i < Hosts; i++) Console.WriteLine($"  host{i}: first build after its enable returned -> {results[i]}");
        Console.WriteLine($"  untracked first builds: {results.Count(r => r.StartsWith("UNTRACKED"))} of {Hosts}; exceptions: {results.Count(r => !r.StartsWith("tracked") && !r.StartsWith("UNTRACKED"))}");
        Console.WriteLine($"  background builds: {bgBuilds}; anomalies (wrong type or exception): {bgAnomalies.Count}{(bgAnomalies.IsEmpty ? "" : " e.g. " + bgAnomalies.First())}");
        Console.WriteLine($"  GetPatchInfo after the storm: {Prefixes(typeof(ProducerBuilder<string, string>))}");
        Measure("after the storm, one build each:");
    }

    // ───────────────────────── E4: the disable path ─────────────────────────
    static async Task Show<K, V>(string when, Message<K, V> m)
    {
        var p = U.Build<K, V>();
        object o = p;
        Console.WriteLine($"  {when}: <{U.N(typeof(K))},{U.N(typeof(V))}> -> {U.N(o)}  ConfluentOwnProducer={U.IsConfluentProducer<K, V>(o)} tracked={o is TrackingKafkaProducer<K, V>} " +
                          $"isinst IProducer<{U.N(typeof(K))},{U.N(typeof(V))}>={o is IProducer<K, V>}");
        try { p.Produce("audit-topic", m); Console.WriteLine("        Produce (no handler) -> ok"); }
        catch (Exception e) { Console.WriteLine($"        Produce (no handler) -> {U.Ex(e)}"); }
        var done = new TaskCompletionSource<DeliveryReport<K, V>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            p.Produce("audit-topic", m, dr => done.TrySetResult(dr));
            var dr = await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Console.WriteLine($"        Produce (handler) -> delivery report arrived: {dr.Error.Code} ({dr.Status})");
        }
        catch (Exception e) { Console.WriteLine($"        Produce (handler) -> {U.Ex(e)}"); }
        p.Dispose();
    }

    public static async Task E4()
    {
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(U.Opt);
        global::Proto.Patch(typeof(ProducerBuilder<string, string>));
        var ss = new Message<string, string> { Key = "k", Value = "v" };
        var ns = new Message<Null, string> { Value = "v" };
        var sb = new Message<string, byte[]> { Key = "k", Value = [1] };
        await Show("enabled <string,string>, prototype patch on", ss);
        KafkaTrackingInterceptor.DisableProducerTracking<string, string>();
        await Show("after DisableProducerTracking<string,string>, patch still on", ss);
        await Show("after DisableProducerTracking<string,string>, patch still on", ns);
        await Show("after DisableProducerTracking<string,string>, patch still on", sb);
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(U.Opt);
        await Show("re-enabled <string,string>", ss);
        KafkaTrackingInterceptor.Reset();
        await Show("after KafkaTrackingInterceptor.Reset()", ss);
        Console.WriteLine($"  prefix calls={(global::Proto.PrefixCalls)}");
    }

    // ───────────────────────── E6: the delivery-reports-off path ─────────────────────────
    public static async Task E6()
    {
        Console.WriteLine("-- (i) Confluent's own Producer<string,string>, dotnet.producer.enable.delivery.reports=false, no interception");
        var p = U.Build<string, string>(U.PCfg(deliveryReports: false));
        Console.WriteLine($"  out-queue length before: {p.Flush(TimeSpan.Zero)}");
        try { p.Produce("audit-topic", new Message<string, string> { Value = "v" }, _ => { }); Console.WriteLine("  Produce(topic, msg, handler) -> ok"); }
        catch (Exception e) { Console.WriteLine($"  Produce(topic, msg, handler) -> {U.Ex(e)}"); }
        Console.WriteLine($"  out-queue length after the refused call: {p.Flush(TimeSpan.Zero)}  (0 = nothing was queued)");
        p.Produce("audit-topic", new Message<string, string> { Value = "v" });
        Console.WriteLine($"  out-queue length after a plain Produce: {p.Flush(TimeSpan.Zero)}");
        var r = await p.ProduceAsync("audit-topic", new Message<string, string> { Value = "v" });
        Console.WriteLine($"  ProduceAsync with reports off -> returns at once: offset={r.Offset} status={r.Status}; out-queue length now {p.Flush(TimeSpan.Zero)}");
        p.Dispose();

        Console.WriteLine("-- (ii) can the prefix read the setting? ProducerBuilder<,>.Config is 'protected internal'; Producer<,> has a private field");
        var b = new ProducerBuilder<string, string>(U.PCfg(deliveryReports: false));
        foreach (var builder in new ProducerBuilder<string, string>[] { b, new SubBuilder(U.PCfg(deliveryReports: false)) })
        {
            var prop = builder.GetType().GetProperty("Config", BindingFlags.NonPublic | BindingFlags.Instance);
            var value = prop is null ? "<property not found>" :
                ((IEnumerable<KeyValuePair<string, string>>)prop.GetValue(builder)!).FirstOrDefault(kv => kv.Key == "dotnet.producer.enable.delivery.reports").Value ?? "<absent>";
            Console.WriteLine($"  {U.N(builder)}: Config found={prop is not null} (getter IsFamilyOrAssembly={prop?.GetMethod?.IsFamilyOrAssembly}) " +
                              $"dotnet.producer.enable.delivery.reports='{value}' -> bool.Parse={(bool.TryParse(value, out var v) ? v.ToString() : "n/a")}");
        }
        var defaultBuilder = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = "localhost:1" });
        var defaultValue = ((IEnumerable<KeyValuePair<string, string>>)defaultBuilder.GetType().GetProperty("Config", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(defaultBuilder)!).FirstOrDefault(kv => kv.Key == "dotnet.producer.enable.delivery.reports").Value;
        Console.WriteLine($"  a builder that never set it: '{defaultValue ?? "<absent>"}' (absent = Confluent's default, reports on)");
        var built = b.SetLogHandler((_, _) => { }).Build();
        var field = built.GetType().GetField("enableDeliveryReports", BindingFlags.NonPublic | BindingFlags.Instance);
        Console.WriteLine($"  built {U.N(built)}: private field enableDeliveryReports={field?.GetValue(built) ?? "<not found>"}");
        built.Dispose();

        Console.WriteLine("-- (iii) a DEPENDENT producer on an owner with delivery reports off, wrapped by the 4.14.6 TrackingKafkaProducer");
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(U.Opt);
        RequestResponseLogger.Clear();
        var ownerOff = U.Build<string, string>(U.PCfg(deliveryReports: false));
        var ownerOn = U.Build<string, string>(U.PCfg(deliveryReports: true));
        var depOffRaw = new DependentProducerBuilder<string, string>(ownerOff.Handle).Build();
        Console.WriteLine($"  dependent producer's own enableDeliveryReports field={depOffRaw.GetType().GetField("enableDeliveryReports", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(depOffRaw)}");
        var depOff = KafkaTrackingInterceptor.WrapProducer(depOffRaw);
        var depOn = KafkaTrackingInterceptor.WrapProducer(new DependentProducerBuilder<string, string>(ownerOn.Handle).Build());
        var ownerOffTracked = KafkaTrackingInterceptor.WrapProducer(ownerOff);
        try { depOff.Produce("dep-of-owner-reports-off", new Message<string, string> { Value = "v" }); Console.WriteLine($"  {U.N(depOff)} (owner reports off).Produce -> ok"); }
        catch (Exception e) { Console.WriteLine($"  dependent (owner reports off).Produce -> {U.Ex(e)}"); }
        try { depOn.Produce("dep-of-owner-reports-on", new Message<string, string> { Value = "v" }); Console.WriteLine($"  {U.N(depOn)} (owner reports on).Produce -> ok"); }
        catch (Exception e) { Console.WriteLine($"  dependent (owner reports on).Produce -> {U.Ex(e)}"); }
        try { ownerOffTracked.Produce("owner-reports-off", new Message<string, string> { Value = "v" }); Console.WriteLine("  tracked owner (reports off).Produce -> ok"); }
        catch (Exception e) { Console.WriteLine($"  tracked owner (reports off).Produce -> {U.Ex(e)}  (F7)"); }
        await Task.Delay(4000); // message.timeout.ms is 1000: a delivery report, where one is made, has arrived
        foreach (var topic in new[] { "dep-of-owner-reports-off", "dep-of-owner-reports-on", "owner-reports-off" })
            Console.WriteLine($"  entries logged for {topic}: {RequestResponseLogger.RequestAndResponseLogs.Count(l => l.Uri.ToString().Contains(topic))}");
        depOff.Dispose(); depOn.Dispose(); ownerOff.Dispose(); ownerOn.Dispose();

        Console.WriteLine("-- (iv) a catch (InvalidOperationException) also catches its subclasses");
        Console.WriteLine($"  ObjectDisposedException is InvalidOperationException: {typeof(InvalidOperationException).IsAssignableFrom(typeof(ObjectDisposedException))}");
    }

    // ───────────────────────── E7: Confluent's internal constructors by version ─────────────────────────
    public static void E7(string[] dlls)
    {
        foreach (var path in dlls)
        {
            var alc = new AssemblyLoadContext("e7-" + Guid.NewGuid(), isCollectible: true);
            try
            {
                var asm = alc.LoadFromAssemblyPath(Path.GetFullPath(path));
                Console.WriteLine($"-- {path}");
                Console.WriteLine($"   assembly {asm.GetName().Version}, informational {asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
                foreach (var tn in new[] { "Confluent.Kafka.Producer`2", "Confluent.Kafka.Consumer`2" })
                {
                    var t = asm.GetType(tn);
                    if (t is null) { Console.WriteLine($"   {tn}: MISSING"); continue; }
                    foreach (var c in t.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                        Console.WriteLine($"   {t.Name} ctor({string.Join(", ", c.GetParameters().Select(p => U.N(p.ParameterType)))})  {(c.IsAssembly ? "internal" : c.IsPublic ? "public" : "non-public")}");
                }
                foreach (var bn in new[] { "Confluent.Kafka.ProducerBuilder`2", "Confluent.Kafka.DependentProducerBuilder`2", "Confluent.Kafka.ConsumerBuilder`2" })
                {
                    var bt = asm.GetType(bn);
                    if (bt is null) { Console.WriteLine($"   {bn}: MISSING"); continue; }
                    var build = bt.GetMethod("Build", Type.EmptyTypes)!;
                    var il = build.GetMethodBody()!.GetILAsByteArray()!;
                    Console.WriteLine($"   {bt.Name}.Build(): virtual={build.IsVirtual} IL={Convert.ToHexString(il)} ({il.Length} bytes)");
                }
                var cfg = asm.GetType("Confluent.Kafka.ProducerBuilder`2")!.GetProperty("Config", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetMethod;
                Console.WriteLine($"   ProducerBuilder`2.Config getter: {(cfg is null ? "MISSING" : cfg.IsFamilyOrAssembly ? "protected internal" : cfg.IsPublic ? "public" : "other")}");
                var f = asm.GetType("Confluent.Kafka.Producer`2")!.GetField("enableDeliveryReports", BindingFlags.Instance | BindingFlags.NonPublic);
                Console.WriteLine($"   Producer`2.enableDeliveryReports field: {(f is null ? "MISSING" : f.FieldType.Name)}");
                var handle = asm.GetType("Confluent.Kafka.Handle")!;
                Console.WriteLine($"   Handle.Owner: {(handle.GetProperty("Owner", BindingFlags.Instance | BindingFlags.NonPublic) is null ? "MISSING" : "present")}; " +
                                  $"Handle.Config: {(handle.GetProperty("Config", BindingFlags.Instance | BindingFlags.NonPublic) is null ? "absent" : "present")}");
                var dpb = asm.GetType("Confluent.Kafka.DependentProducerBuilder`2")!;
                Console.WriteLine($"   DependentProducerBuilder`2 public properties: {string.Join(", ", dpb.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(p => p.Name))}");
            }
            catch (Exception e) { Console.WriteLine($"-- {path}: {U.Ex(e)}"); }
            finally { alc.Unload(); }
        }
    }

    // ───────────────────────── E8: duplicate tracker registration ─────────────────────────
    static void PrintRegistry(string label)
    {
        var reg = TrackingComponentRegistry.GetRegisteredComponents();
        var unused = TrackingComponentRegistry.GetUnusedComponents();
        Console.WriteLine($"  {label}: registered={reg.Count} unused={unused.Count}");
        foreach (var c in reg) Console.WriteLine($"     {c.ComponentName} invoked={c.WasInvoked} count={c.InvocationCount}");
        if (unused.Count > 0)
            Console.WriteLine($"  -> ReportDiagnostics.Analyse would print: \"Warning: {unused.Count} tracking component(s) were registered but never invoked: " +
                              $"{string.Join(", ", unused.Select(c => c.ComponentName))}. This usually means the component was added to the wrong pipeline or options. ...\"");
    }

    static async Task ProduceOnce(IProducer<string, string> p, string topic)
    {
        var done = new TaskCompletionSource<DeliveryReport<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        p.Produce(topic, new Message<string, string> { Key = "k", Value = "v" }, dr => done.TrySetResult(dr));
        var dr = await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine($"  produced once through {U.N(p)} on {topic}: delivery report {dr.Error.Code} (no broker; the wrapped handler logs it)");
    }

    public static async Task E8a()
    {
        TrackingComponentRegistry.Clear();
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(o => o.CurrentTestInfoFetcher = () => ("t", "id"));
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(o => o.CurrentTestInfoFetcher = () => ("t", "id"));
        PrintRegistry("after KafkaTrackingInterceptor.EnableProducerTracking<string,string>() twice");
        var wrapped = KafkaTrackingInterceptor.WrapProducer(U.Build<string, string>()); // no Harmony patch in this process
        await ProduceOnce(wrapped, "e8a");
        PrintRegistry("after one wrapped producer produced once");
        wrapped.Dispose();
    }

    public static async Task E8b()
    {
        TrackingComponentRegistry.Clear();
        KafkaBuildInterceptor.EnableTracking<string, string>(o => o.CurrentTestInfoFetcher = () => ("t", "id")); // shipped: consumer + producer
        PrintRegistry("after KafkaBuildInterceptor.EnableTracking<string,string>() (both directions)");
        var p = U.Build<string, string>(); // intercepted by the shipped patch
        await ProduceOnce(p, "e8b");
        PrintRegistry("after an app that only produces built one producer and produced once");
        p.Dispose();
    }

    public static async Task E8c()
    {
        TrackingComponentRegistry.Clear();
        RequestResponseLogger.Clear();
        for (var i = 0; i < 10; i++)
        {
            var host = i;
            KafkaBuildInterceptor.EnableProducerTracking<string, string>(o => { o.CallerName = $"host{host}"; o.CurrentTestInfoFetcher = () => ("t", "id"); });
        }
        PrintRegistry("ten in-process hosts each called KafkaBuildInterceptor.EnableProducerTracking<string,string>() with its own CallerName");
        var producers = Enumerable.Range(0, 10).Select(_ => U.Build<string, string>()).ToList();
        for (var i = 0; i < producers.Count; i++) await ProduceOnce(producers[i], $"e8c-host{i}");
        PrintRegistry("after each host built a producer and produced once");
        var callers = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.Type == RequestResponseType.Request).Select(l => l.CallerName).Distinct();
        Console.WriteLine($"  CallerName on the ten hosts' produce entries: {string.Join(", ", callers)}");
        producers.ForEach(p => p.Dispose());
    }
}
