#:package System.Reflection.MetadataLoadContext@10.0.0
// Which of an SDK client's virtual members a Kronikol wrapper leaves to the base (plans/CLIENT_FLOORS_PLAN.md R2b).
// A Tracking* class that derives from an SDK client calls the client's protected parameterless constructor, the one
// for mocking, so every virtual member it does not override runs against an empty client: on Pub/Sub 3.35.0 that made
// StopAsync(ShutdownOptions, CancellationToken) throw (F8). This lists, for each such class, the base's public and
// protected virtual members it does not override, read from the assemblies without loading them.
//   dotnet run --file wrappers.cs -- <bin dir with the extensions> [<SDK dll to read in place of the bin's copy>...]
using System.Reflection;
using System.Runtime.InteropServices;

var bin = args[0];
var overrides = args.Skip(1).ToDictionary(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
var paths = Directory.GetFiles(bin, "*.dll").Where(p => !overrides.ContainsKey(Path.GetFileName(p)))
    .Concat(overrides.Values)
    .Concat(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
    .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).Select(g => g.First());
using var context = new MetadataLoadContext(new PathAssemblyResolver(paths));
const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

static string Key(MethodInfo m) =>
    $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})";

foreach (var file in Directory.GetFiles(bin, "Kronikol.Extensions.*.dll").OrderBy(f => f))
{
    Type[] types;
    try { types = context.LoadFromAssemblyPath(file).GetTypes(); }
    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
    foreach (var type in types.Where(t => t.IsClass && t.BaseType?.Namespace is { } ns
                 && (ns.StartsWith("Azure") || ns.StartsWith("Google") || ns.StartsWith("Confluent") || ns.StartsWith("Microsoft.Azure"))))
    {
        var sdk = type.BaseType!;
        var overridden = new HashSet<string>();
        for (var t = type; t is not null && t != sdk; t = t.BaseType)
            foreach (var m in t.GetMethods(Declared).Where(m => m.IsVirtual))
                overridden.Add(Key(m));
        var left = new List<string>();
        for (var t = sdk; t is not null && t.FullName != "System.Object"; t = t.BaseType)
            foreach (var m in t.GetMethods(Declared).Where(m => m.IsVirtual && !m.IsFinal && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly)))
                if (overridden.Add(Key(m)))
                    left.Add($"{(m.IsAbstract ? "abstract " : "")}{t.Name}.{Key(m)}");
        var version = sdk.Assembly.GetName().Version;
        Console.WriteLine($"{type.FullName} : {sdk.FullName} ({sdk.Assembly.GetName().Name} {version})");
        foreach (var m in left.OrderBy(m => m))
            Console.WriteLine($"  not overridden: {m}");
    }
}
