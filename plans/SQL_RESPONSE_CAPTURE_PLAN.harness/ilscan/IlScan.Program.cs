// IlScan: finds every place in EF Core provider assemblies that depends on the CONCRETE type of an
// ADO.NET object (above all the DbDataReader), by reading the IL with Mono.Cecil.
//
// Usage:
//   dotnet run --project scanner -- <pkgs-dir> [--out <dir>] [--scan-all] [--only <projectDirName>...]
//
// <pkgs-dir> holds one restore-only project per package group (pkgs/<name>/obj/project.assets.json).
// For each project the scanner reads project.assets.json, takes every assembly of the restored graph
// from the NuGet cache, scans the EF Core provider assemblies (package ids below) and resolves operand
// types through the rest of the graph (Npgsql, Microsoft.Data.SqlClient, Microsoft.Data.Sqlite,
// MySqlConnector, MySql.Data, Oracle.ManagedDataAccess, Google.Cloud.Spanner.Data, FirebirdClient...)
// plus the shared framework.
//
// What it reports, per assembly:
//   A. castclass / isinst / unbox / unbox.any whose operand derives from System.Data.Common.DbDataReader
//      (any type other than DbDataReader itself), and, in a separate section, from DbCommand,
//      DbConnection, DbParameter, DbParameterCollection, DbTransaction, DbBatch, DbBatchCommand, ...
//   B. ldtoken of such a reader type (typeof(X)), or of a method/field declared on one, with the
//      nearby ldstr and the reflection / Expression call that consumes the token.
//   C. call / callvirt / newobj / ldftn / ldvirtftn / jmp to a member declared on a reader type, or
//      whose signature or generic arguments mention one; field access on one; any other instruction
//      whose type operand is a reader type (newarr, box, constrained., ...).
//   D. Declarations (fields, parameters, returns, locals, base types, generic constraints) that use a
//      reader type, and ldstr literals that mention "DataReader" (string-based reflection).
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

var argList = args.ToList();
if (argList.Count == 0 || argList[0].StartsWith("--"))
{
    Console.Error.WriteLine("usage: IlScan <pkgs-dir> [--out <dir>] [--scan-all] [--only <name>...]");
    return 2;
}

string pkgsDir = Path.GetFullPath(argList[0]);
string outDir = Path.GetFullPath(Path.Combine(pkgsDir, "..", "out"));
bool scanAll = false;
var only = new List<string>();
var dumps = new List<string>();
var callPatterns = new List<string>();
var typeInfoPatterns = new List<string>();
for (int i = 1; i < argList.Count; i++)
{
    switch (argList[i])
    {
        case "--out": outDir = Path.GetFullPath(argList[++i]); break;
        case "--scan-all": scanAll = true; break;
        case "--only": while (i + 1 < argList.Count && !argList[i + 1].StartsWith("--")) only.Add(argList[++i]); break;
        case "--dump": dumps.Add(argList[++i]); break;      // print the IL of methods whose full name contains this text
        case "--calls": callPatterns.Add(argList[++i]); break; // print call sites whose callee full name matches this regex
        case "--typeinfo": typeInfoPatterns.Add(argList[++i]); break; // print sealed/abstract/base/ctors of types matching this regex (all assemblies)
        default: Console.Error.WriteLine($"unknown argument {argList[i]}"); return 2;
    }
}

Directory.CreateDirectory(outDir);
var report = new StringBuilder();
var allHits = new List<Hit>();
var unresolvedAll = new SortedSet<string>();
var runtimeReflectionAll = new List<string>();

var projects = Directory.GetDirectories(pkgsDir)
    .Where(d => File.Exists(Path.Combine(d, "obj", "project.assets.json")))
    .Where(d => only.Count == 0 || only.Contains(Path.GetFileName(d)))
    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
    .ToList();

if (projects.Count == 0)
{
    Console.Error.WriteLine($"no restored projects under {pkgsDir}");
    return 1;
}

if (typeInfoPatterns.Count > 0)
{
    foreach (var proj in projects)
        Console.Write(new ProjectScanner(proj, scanAll).TypeInfo(typeInfoPatterns));
    return 0;
}

if (dumps.Count > 0 || callPatterns.Count > 0)
{
    foreach (var proj in projects)
    {
        var scanner = new ProjectScanner(proj, scanAll);
        Console.Write(scanner.DumpAndCalls(dumps, callPatterns));
    }
    return 0;
}

foreach (var proj in projects)
{
    var scanner = new ProjectScanner(proj, scanAll);
    scanner.Run();
    allHits.AddRange(scanner.Hits);
    foreach (var u in scanner.Unresolved) unresolvedAll.Add($"{scanner.Name}: {u}");
    runtimeReflectionAll.AddRange(scanner.RuntimeReflection);
    report.Append(scanner.RenderReport());
}

// Cross-project summary.
var summary = new StringBuilder();
summary.AppendLine("##########################################################################################");
summary.AppendLine("# SUMMARY 1: READER dependencies (role=dependency): casts, typeof() feeding reflection, calls to");
summary.AppendLine("#            reader-only members, per provider assembly version");
summary.AppendLine("##########################################################################################");
foreach (var g in allHits
             .Where(h => h.Category == "Reader" && h.Role == "dependency")
             .GroupBy(h => (h.PackageId, h.PackageVersion, h.AssemblyName, h.AssemblyVersion, h.InformationalVersion))
             .OrderBy(g => g.Key.PackageId).ThenBy(g => g.Key.PackageVersion))
{
    summary.AppendLine($"{g.Key.AssemblyName} {g.Key.AssemblyVersion}  (package {g.Key.PackageId} {g.Key.PackageVersion}, info {g.Key.InformationalVersion})");
    foreach (var h in g.OrderBy(h => h.LogicalMethod).ThenBy(h => h.Offset))
    {
        summary.AppendLine($"    {h.Kind,-8} {h.LogicalMethod}  IL_{h.Offset:x4} {h.OpCode} {h.Operand}{(h.Nearby.Length > 0 ? "   <" + h.Nearby + ">" : "")}");
        if (h.Kind == "cast") summary.AppendLine($"             ctx: {h.Context}");
    }
}
summary.AppendLine();
summary.AppendLine("##########################################################################################");
summary.AppendLine("# SUMMARY 2: other READER references (not root causes): scaffolding code on raw ADO.NET, calls whose");
summary.AppendLine("#            signature returns/accepts a concrete reader, field carries. Distinct member x count.");
summary.AppendLine("##########################################################################################");
foreach (var g in allHits
             .Where(h => h.Category == "Reader" && h.Role != "dependency")
             .GroupBy(h => (h.PackageId, h.PackageVersion, h.AssemblyName))
             .OrderBy(g => g.Key.PackageId).ThenBy(g => g.Key.PackageVersion))
{
    summary.AppendLine($"{g.Key.AssemblyName}  (package {g.Key.PackageId} {g.Key.PackageVersion})");
    foreach (var m in g.GroupBy(h => (h.Role, Type: OuterType(h.LogicalMethod), Reader: ReaderTypeOf(h.Chain)))
                 .OrderBy(m => m.Key.Role).ThenBy(m => m.Key.Type))
        summary.AppendLine($"    {m.Key.Role,-11} x{m.Count(),-4} in {m.Key.Type}  ->  {m.Key.Reader}   [{string.Join(",", m.Select(h => h.Kind).Distinct())}; e.g. {m.First().OpCode} {Trunc(m.First().Operand, 140)}]");
}
summary.AppendLine();
summary.AppendLine("##########################################################################################");
summary.AppendLine("# SUMMARY 3: OTHER ADO.NET concrete-type casts (Command/Connection/Parameter/Transaction/...)");
summary.AppendLine("##########################################################################################");
foreach (var g in allHits
             .Where(h => h.Category != "Reader" && h.Kind == "cast")
             .GroupBy(h => (h.PackageId, h.PackageVersion, h.AssemblyName))
             .OrderBy(g => g.Key.PackageId).ThenBy(g => g.Key.PackageVersion))
{
    summary.AppendLine($"{g.Key.AssemblyName}  (package {g.Key.PackageId} {g.Key.PackageVersion})");
    foreach (var h in g.OrderBy(h => h.Category).ThenBy(h => h.LogicalMethod).ThenBy(h => h.Offset))
        summary.AppendLine($"    {h.Category,-12} {h.Role,-11} {h.LogicalMethod}  IL_{h.Offset:x4} {h.OpCode} {h.Operand}");
}
summary.AppendLine();
summary.AppendLine("##########################################################################################");
summary.AppendLine("# SUMMARY 4: casts to interfaces only concrete ADO.NET types implement (not their System.Data.Common");
summary.AppendLine("#            base), and DbDataReaderExtensions.GetColumnSchema/CanGetColumnSchema calls");
summary.AppendLine("##########################################################################################");
foreach (var g in allHits
             .Where(h => h.Category.EndsWith("Interface"))
             .GroupBy(h => (h.PackageId, h.PackageVersion, h.AssemblyName))
             .OrderBy(g => g.Key.PackageId).ThenBy(g => g.Key.PackageVersion))
{
    summary.AppendLine($"{g.Key.AssemblyName}  (package {g.Key.PackageId} {g.Key.PackageVersion})");
    foreach (var h in g.OrderBy(h => h.LogicalMethod).ThenBy(h => h.Offset))
    {
        summary.AppendLine($"    {h.Category,-20} {h.Role,-11} {h.LogicalMethod}  IL_{h.Offset:x4} {h.OpCode} {h.Operand}");
        summary.AppendLine($"             ctx: {h.Context}");
    }
}
summary.AppendLine();
summary.AppendLine("##########################################################################################");
summary.AppendLine("# SUMMARY 5: name-based reflection on an object's runtime type (obj.GetType().GetX(\"name\")) in provider");
summary.AppendLine("#            assemblies; the receiver may or may not be an ADO.NET object (check ctx)");
summary.AppendLine("##########################################################################################");
foreach (var line in runtimeReflectionAll.Distinct()) summary.AppendLine("    " + line);
summary.AppendLine();
summary.AppendLine("Assemblies with NO reader-type dependency at all are listed in each project's section ('clean').");
summary.AppendLine();
if (unresolvedAll.Count > 0)
{
    summary.AppendLine("Unresolved type references (could not be checked; see per-project sections):");
    foreach (var u in unresolvedAll) summary.AppendLine("    " + u);
}
else
{
    summary.AppendLine("Unresolved type references: none (every castclass/isinst/ldtoken/member operand was resolved).");
}

string reportText = summary + Environment.NewLine + report;
File.WriteAllText(Path.Combine(outDir, "report.txt"), reportText);

var tsv = new StringBuilder();
tsv.AppendLine(string.Join('\t', "project", "package", "packageVersion", "assembly", "assemblyVersion", "informationalVersion",
    "category", "role", "kind", "containingType", "method", "logicalMethod", "offset", "opcode", "operand", "operandAssembly", "nearby", "context"));
foreach (var h in allHits)
    tsv.AppendLine(string.Join('\t', h.Project, h.PackageId, h.PackageVersion, h.AssemblyName, h.AssemblyVersion, h.InformationalVersion,
        h.Category, h.Role, h.Kind, h.ContainingType, h.Method, h.LogicalMethod, $"IL_{h.Offset:x4}", h.OpCode, h.Operand, h.OperandAssembly,
        h.Nearby, h.Context).Replace("\r", " ").Replace("\n", " "));
File.WriteAllText(Path.Combine(outDir, "hits.tsv"), tsv.ToString());

Console.WriteLine(summary.ToString());
Console.WriteLine($"Full report: {Path.Combine(outDir, "report.txt")}");
Console.WriteLine($"All hits (TSV): {Path.Combine(outDir, "hits.tsv")}");
return 0;

static string OuterType(string logical)
{
    var head = logical.Split(" [")[0];
    int dot = head.LastIndexOf('.');
    return dot > 0 ? head[..dot] : head;
}

static string ReaderTypeOf(string chain)
{
    foreach (var prefix in new[] { "declared on ", "generic argument of the declaring type: ", "generic argument: ", "signature mentions " })
        if (chain.StartsWith(prefix)) chain = chain[prefix.Length..];
    int arrow = chain.IndexOf(" -> ", StringComparison.Ordinal);
    return arrow > 0 ? chain[..arrow] : chain;
}

static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "...";

record Hit(
    string Project, string PackageId, string PackageVersion, string AssemblyName, string AssemblyVersion, string InformationalVersion,
    string Category, string Kind, string ContainingType, string Method, string LogicalMethod, int Offset, string OpCode,
    string Operand, string OperandAssembly, string Chain, string Nearby, string Context, string Role);

record AssetFile(string PackageId, string PackageVersion, string Kind, string Path);

sealed class ProjectScanner
{
    // EF Core provider packages: the assemblies that receive the (possibly substituted) reader.
    static readonly string[] ProviderPackagePrefixes =
    {
        "Microsoft.EntityFrameworkCore", "Npgsql.EntityFrameworkCore", "Pomelo.EntityFrameworkCore",
        "MySql.EntityFrameworkCore", "MySql.Data.EntityFrameworkCore", "Oracle.EntityFrameworkCore",
        "FirebirdSql.EntityFrameworkCore", "Google.Cloud.EntityFrameworkCore",
    };

    // System.Data.Common base types: a type deriving from one of these (other than the base itself)
    // is "a concrete ADO.NET type"; a cast to it fails for a decorator that only derives from the base.
    static readonly Dictionary<string, string> BaseCategories = new()
    {
        ["System.Data.Common.DbDataReader"] = "Reader",
        ["System.Data.Common.DbCommand"] = "Command",
        ["System.Data.Common.DbConnection"] = "Connection",
        ["System.Data.Common.DbParameter"] = "Parameter",
        ["System.Data.Common.DbParameterCollection"] = "ParameterCollection",
        ["System.Data.Common.DbTransaction"] = "Transaction",
        ["System.Data.Common.DbBatch"] = "Batch",
        ["System.Data.Common.DbBatchCommand"] = "BatchCommand",
        ["System.Data.Common.DbBatchCommandCollection"] = "BatchCommandCollection",
        ["System.Data.Common.DbDataSource"] = "DataSource",
        ["System.Data.Common.DbProviderFactory"] = "ProviderFactory",
        ["System.Data.Common.DbConnectionStringBuilder"] = "ConnectionStringBuilder",
        ["System.Data.Common.DbException"] = "Exception",
    };

    readonly string _dir;
    readonly bool _scanAll;
    public string Name { get; }
    public List<Hit> Hits { get; } = new();
    public SortedSet<string> Unresolved { get; } = new();
    readonly List<(AssetFile File, AssemblyDefinition Asm, string InfoVersion, bool Symbols)> _scanned = new();
    readonly List<string> _clean = new();
    readonly Dictionary<string, (string Cat, string Chain)?> _classifyCache = new();
    readonly List<string> _declarations = new();
    readonly List<string> _strings = new();
    public List<string> RuntimeReflection { get; } = new();
    MapResolver _resolver = null!;

    public ProjectScanner(string dir, bool scanAll)
    {
        _dir = dir;
        _scanAll = scanAll;
        Name = Path.GetFileName(dir);
    }

    // --dump / --calls: IL listings for verification (no classification).
    public string DumpAndCalls(List<string> dumps, List<string> callPatterns)
    {
        var sb = new StringBuilder();
        var files = ReadAssets(Path.Combine(_dir, "obj", "project.assets.json"));
        _resolver = new MapResolver(files, FindSharedFramework());
        var regexes = callPatterns.Select(p => new Regex(p, RegexOptions.Compiled)).ToList();
        foreach (var f in files.Where(f => f.Kind == "runtime" && (_scanAll ? !IsFramework(f.PackageId) : IsProvider(f.PackageId)))
                     .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            var (asm, _) = Load(f.Path);
            foreach (var type in asm.MainModule.GetTypes())
            {
                foreach (var fd in type.Fields.Where(fd => regexes.Any(r => r.IsMatch(fd.FullName))))
                    sb.AppendLine($"[{Name}] DEFINITION field {fd.FullName} in {asm.Name.Name}");
                foreach (var pd in type.Properties.Where(pd => regexes.Any(r => r.IsMatch(pd.FullName))))
                    sb.AppendLine($"[{Name}] DEFINITION property {pd.FullName} in {asm.Name.Name}");
            }
            foreach (var type in asm.MainModule.GetTypes())
            foreach (var m in type.Methods.Where(m => m.HasBody))
            {
                var ins = m.Body.Instructions;
                if (dumps.Any(d => m.FullName.Contains(d, StringComparison.Ordinal)))
                {
                    sb.AppendLine($"===== [{Name}] {asm.Name.Name} {asm.Name.Version} ({f.PackageId} {f.PackageVersion})");
                    sb.AppendLine($"===== {m.FullName}");
                    foreach (var v in m.Body.Variables) sb.AppendLine($"      .local V_{v.Index} {v.VariableType.FullName}");
                    foreach (var h in m.Body.ExceptionHandlers)
                        sb.AppendLine($"      .try IL_{h.TryStart.Offset:x4}-IL_{(h.TryEnd?.Offset ?? 0):x4} {h.HandlerType} {h.CatchType?.FullName} handler IL_{h.HandlerStart.Offset:x4}-IL_{(h.HandlerEnd?.Offset ?? 0):x4}");
                    foreach (var x in ins) sb.AppendLine("      " + Long(x));
                    sb.AppendLine();
                }
                if (regexes.Count == 0) continue;
                if (regexes.Any(r => r.IsMatch(m.FullName))) sb.AppendLine($"[{Name}] DEFINITION method {m.FullName} in {asm.Name.Name}");
                for (int i = 0; i < ins.Count; i++)
                {
                    if (ins[i].Operand is not MemberReference mr) continue;            // methods and fields
                    if (mr is MethodReference && ins[i].OpCode.FlowControl != FlowControl.Call && ins[i].OpCode.Code != Code.Ldftn && ins[i].OpCode.Code != Code.Ldvirtftn) continue;
                    if (!regexes.Any(r => r.IsMatch(mr.FullName))) continue;
                    string args = string.Join(" | ", Enumerable.Range(Math.Max(0, i - 4), Math.Min(4, i)).Select(k => Short(ins[k])));
                    sb.AppendLine($"[{Name}] {asm.Name.Name} {asm.Name.Version}  {Logical(m)}  IL_{ins[i].Offset:x4} {ins[i].OpCode.Name} {mr.FullName}");
                    sb.AppendLine($"        preceded by: {args}");
                }
            }
        }
        return sb.ToString();
    }

    public string TypeInfo(List<string> patterns)
    {
        var sb = new StringBuilder();
        var files = ReadAssets(Path.Combine(_dir, "obj", "project.assets.json"));
        _resolver = new MapResolver(files, FindSharedFramework());
        var regexes = patterns.Select(p => new Regex(p)).ToList();
        foreach (var path in files.Where(f => f.Kind == "runtime").Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AssemblyDefinition a;
            try { a = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = _resolver }); } catch { continue; }
            foreach (var t in a.MainModule.GetTypes().Where(t => regexes.Any(r => r.IsMatch(t.FullName))))
            {
                var ctors = t.Methods.Where(m => m.IsConstructor && !m.IsStatic).Select(m => (m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "private") + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.Name)) + ")");
                sb.AppendLine($"[{Name}] {t.FullName} [{a.Name.Name} {a.Name.Version}] {(t.IsPublic || t.IsNestedPublic ? "public" : "non-public")} {(t.IsSealed ? "SEALED" : "not sealed")}{(t.IsAbstract ? " abstract" : "")} : {t.BaseType?.FullName}  ctors: {string.Join("; ", ctors)}");
            }
        }
        return sb.ToString();
    }

    static string Long(Instruction x)
    {
        string op = x.Operand switch
        {
            null => "",
            MethodReference mr => mr.FullName,
            FieldReference fr => fr.FullName,
            TypeReference tr => tr.FullName,
            string s => "\"" + s + "\"",
            Instruction t => $"IL_{t.Offset:x4}",
            Instruction[] ts => string.Join(",", ts.Select(t => $"IL_{t.Offset:x4}")),
            VariableDefinition v => $"V_{v.Index}",
            ParameterDefinition p => p.Name,
            _ => x.Operand.ToString() ?? "",
        };
        return $"IL_{x.Offset:x4}: {x.OpCode.Name} {op}".TrimEnd();
    }

    public void Run()
    {
        var files = ReadAssets(Path.Combine(_dir, "obj", "project.assets.json"));
        _resolver = new MapResolver(files, FindSharedFramework());
        CollectExtraInterfaces(files);

        var toScan = files
            .Where(f => f.Kind == "runtime")
            .Where(f => _scanAll ? !IsFramework(f.PackageId) : IsProvider(f.PackageId))
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(f => f.PackageId).ThenBy(f => f.Path)
            .ToList();

        foreach (var f in toScan)
        {
            var (asm, symbols) = Load(f.Path);
            string info = asm.CustomAttributes
                .Where(a => a.AttributeType.FullName == "System.Reflection.AssemblyInformationalVersionAttribute")
                .Select(a => a.ConstructorArguments[0].Value as string).FirstOrDefault() ?? "";
            _scanned.Add((f, asm, info, symbols));
            int before = Hits.Count(h => h.Category == "Reader");
            int declBefore = _declarations.Count;
            ScanAssembly(f, asm, info);
            if (Hits.Count(h => h.Category == "Reader") == before && _declarations.Count == declBefore)
                _clean.Add($"{asm.Name.Name} {asm.Name.Version} ({f.PackageId} {f.PackageVersion})");
        }
    }

    // Interfaces that a concrete ADO.NET type in the closure implements but its System.Data.Common base does
    // not (IDbColumnSchemaGenerator on NpgsqlDataReader/SqlDataReader, ICloneable on SqlCommand, ...). A cast
    // to one of these fails for a decorator that derives only from the base.
    readonly Dictionary<string, (string Cat, SortedSet<string> Implementers)> _extraInterfaces = new();
    public List<string> ExtraInterfaceNotes { get; } = new();

    void CollectExtraInterfaces(List<AssetFile> files)
    {
        var sdc = _resolver.Resolve(new AssemblyNameReference("System.Data.Common", new Version(0, 0)));
        if (sdc == null) { ExtraInterfaceNotes.Add("System.Data.Common not found; interface check skipped"); return; }
        var baseIfaces = new Dictionary<string, HashSet<string>>();
        foreach (var (baseName, cat) in BaseCategories)
        {
            var bt = sdc.MainModule.GetType(baseName);
            if (bt == null) { ExtraInterfaceNotes.Add($"{baseName} not found in System.Data.Common"); continue; }
            baseIfaces[cat] = AllInterfaces(bt);
        }
        var paths = files.Where(f => f.Kind == "runtime" || f.Kind == "compile")
            .Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int types = 0;
        foreach (var path in paths)
        {
            AssemblyDefinition a;
            try { a = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = _resolver, ReadingMode = ReadingMode.Deferred }); }
            catch { continue; }
            foreach (var t in a.MainModule.GetTypes())
            {
                if (t.IsInterface) continue;
                var c = Classify(t);
                if (c == null || c.Value.Cat == "Exception" || !baseIfaces.TryGetValue(c.Value.Cat, out var bi)) continue;
                types++;
                foreach (var iface in AllInterfaces(t))
                {
                    if (bi.Contains(iface)) continue;
                    // General-purpose BCL interfaces (IDisposable, ICloneable, collection interfaces) are tested on
                    // all kinds of objects (every foreach), so a hit on them says nothing about ADO.NET objects.
                    if (iface.StartsWith("System.", StringComparison.Ordinal) && !iface.StartsWith("System.Data", StringComparison.Ordinal)) continue;
                    if (!_extraInterfaces.TryGetValue(iface, out var e)) _extraInterfaces[iface] = e = (c.Value.Cat, new SortedSet<string>());
                    else if (!e.Cat.Split('/').Contains(c.Value.Cat)) _extraInterfaces[iface] = e = (e.Cat + "/" + c.Value.Cat, e.Implementers);
                    e.Implementers.Add($"{t.FullName} [{a.Name.Name}]");
                }
            }
        }
        ExtraInterfaceNotes.Add($"{types} concrete ADO.NET types examined; {_extraInterfaces.Count} interfaces beyond their System.Data.Common base:");
        foreach (var (iface, (cat, impl)) in _extraInterfaces.OrderBy(k => k.Value.Cat).ThenBy(k => k.Key))
            ExtraInterfaceNotes.Add($"    {cat,-12} {iface}  <- {string.Join(", ", impl.Take(4))}{(impl.Count > 4 ? $" (+{impl.Count - 4})" : "")}");
    }

    static HashSet<string> AllInterfaces(TypeDefinition td)
    {
        var result = new HashSet<string>();
        var stack = new Stack<TypeReference>();
        for (TypeDefinition? t = td; t != null; t = t.BaseType == null ? null : SafeResolve(t.BaseType))
            foreach (var ii in t.Interfaces) stack.Push(ii.InterfaceType);
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            if (!result.Add(i.FullName)) continue;
            var id = SafeResolve(i);
            if (id != null) foreach (var ii in id.Interfaces) stack.Push(ii.InterfaceType);
        }
        return result;
    }

    static bool IsProvider(string id) => ProviderPackagePrefixes.Any(p => id.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    static bool IsFramework(string id) =>
        id.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("Microsoft.NETCore.", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("Microsoft.Win32.", StringComparison.OrdinalIgnoreCase);

    static string FindSharedFramework()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.NETCore.App");
        var best = Directory.GetDirectories(root)
            .Select(d => (Dir: d, Ok: Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v), V: v))
            .Where(x => x.Ok && x.V!.Major == 10)
            .OrderByDescending(x => x.V)
            .FirstOrDefault();
        if (best.Dir == null) throw new InvalidOperationException("no Microsoft.NETCore.App 10.x shared framework found");
        return best.Dir;
    }

    static List<AssetFile> ReadAssets(string assetsPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = doc.RootElement;
        var folders = root.GetProperty("packageFolders").EnumerateObject().Select(p => p.Name).ToList();
        var libraries = root.GetProperty("libraries");
        var target = root.GetProperty("targets").EnumerateObject().First(t => !t.Name.Contains('/'));
        var result = new List<AssetFile>();
        foreach (var lib in target.Value.EnumerateObject())
        {
            if (lib.Value.GetProperty("type").GetString() != "package") continue;
            var parts = lib.Name.Split('/');
            string id = parts[0], ver = parts[1];
            string rel = libraries.GetProperty(lib.Name).GetProperty("path").GetString()!;
            string? pkgRoot = folders.Select(f => Path.Combine(f, rel)).FirstOrDefault(Directory.Exists);
            if (pkgRoot == null) continue;
            foreach (var kind in new[] { "runtime", "compile" })
            {
                if (!lib.Value.TryGetProperty(kind, out var items)) continue;
                foreach (var item in items.EnumerateObject())
                {
                    if (!item.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(new AssetFile(id, ver, kind, Path.GetFullPath(Path.Combine(pkgRoot, item.Name))));
                }
            }
            if (lib.Value.TryGetProperty("runtimeTargets", out var rts))
            {
                foreach (var item in rts.EnumerateObject())
                {
                    if (!item.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                    string rid = item.Value.TryGetProperty("rid", out var r) ? r.GetString() ?? "" : "";
                    result.Add(new AssetFile(id, ver, "runtimeTarget:" + rid, Path.GetFullPath(Path.Combine(pkgRoot, item.Name))));
                }
            }
        }
        return result;
    }

    (AssemblyDefinition, bool) Load(string path)
    {
        try
        {
            var rp = new ReaderParameters
            {
                AssemblyResolver = _resolver,
                ReadSymbols = true,
                SymbolReaderProvider = new DefaultSymbolReaderProvider(throwIfNoSymbol: false),
            };
            var asm = AssemblyDefinition.ReadAssembly(path, rp);
            return (asm, asm.MainModule.HasSymbols);
        }
        catch
        {
            var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = _resolver });
            return (asm, false);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Classification
    // ---------------------------------------------------------------------------------------------

    (string Cat, string Chain)? Classify(TypeReference? tr)
    {
        if (tr == null) return null;
        while (tr is TypeSpecification spec && tr is not GenericInstanceType) tr = spec.ElementType; // arrays, byref, pointers, modifiers
        if (tr == null || tr is FunctionPointerType) return null;  // function pointers have no element type
        if (tr is GenericInstanceType git)
        {
            // A generic instance is a concrete ADO type if its element type is; its arguments are checked separately.
            tr = git.ElementType;
        }
        if (tr is GenericParameter) return null;
        if (BaseCategories.ContainsKey(tr.FullName)) return null; // the System.Data.Common base itself is fine

        string key = (tr.Scope?.Name ?? "?") + "|" + tr.FullName;
        if (_classifyCache.TryGetValue(key, out var cached)) return cached;

        (string, string)? result = null;
        var chain = new List<string> { tr.FullName };
        TypeDefinition? td = SafeResolve(tr);
        if (td == null)
        {
            if (!tr.FullName.StartsWith("System.") && !tr.FullName.StartsWith("<")) Unresolved.Add($"{tr.FullName} [{tr.Scope?.Name}]");
        }
        else
        {
            var b = td.BaseType;
            int depth = 0;
            while (b != null && depth++ < 32)
            {
                var bn = b is GenericInstanceType bg ? bg.ElementType.FullName : b.FullName;
                chain.Add(bn);
                if (BaseCategories.TryGetValue(bn, out var cat)) { result = (cat, string.Join(" -> ", chain)); break; }
                var bd = SafeResolve(b);
                if (bd == null)
                {
                    if (!bn.StartsWith("System.")) Unresolved.Add($"{bn} [{b.Scope?.Name}] (base of {td.FullName})");
                    break;
                }
                b = bd.BaseType;
            }
        }
        _classifyCache[key] = result;
        return result;
    }

    static TypeDefinition? SafeResolve(TypeReference tr)
    {
        try { return tr.Resolve(); }
        catch { return null; }
    }

    // Every category a type reference touches, including generic arguments (List<NpgsqlDataReader>).
    IEnumerable<(string Cat, string Chain, TypeReference Type)> ClassifyDeep(TypeReference? tr, int depth = 0)
    {
        if (tr == null || depth > 8) yield break;
        var c = Classify(tr);
        if (c != null) yield return (c.Value.Cat, c.Value.Chain, tr);
        TypeReference t = tr;
        while (t is TypeSpecification spec && t is not GenericInstanceType && spec.ElementType != null) t = spec.ElementType;
        if (t is GenericInstanceType git)
            foreach (var a in git.GenericArguments)
                foreach (var x in ClassifyDeep(a, depth + 1))
                    yield return x;
    }

    static string ScopeOf(TypeReference tr)
    {
        var t = tr;
        while (t is TypeSpecification spec && spec.ElementType != null) t = spec.ElementType;
        var td = SafeResolve(t);
        if (td != null) return $"{td.Module.Assembly.Name.Name} {td.Module.Assembly.Name.Version}";
        return t.Scope?.Name ?? "?";
    }

    // ---------------------------------------------------------------------------------------------
    // Scanning
    // ---------------------------------------------------------------------------------------------

    void ScanAssembly(AssetFile f, AssemblyDefinition asm, string info)
    {
        foreach (var module in asm.Modules)
        foreach (var type in module.GetTypes())
        {
            // Declarations on the type.
            foreach (var c in ClassifyDeep(type.BaseType).Where(x => x.Cat == "Reader"))
                _declarations.Add($"{asm.Name.Name}: type {type.FullName} has base type {c.Type.FullName}");
            foreach (var gp in type.GenericParameters)
            foreach (var con in gp.Constraints)
            foreach (var c in ClassifyDeep(con.ConstraintType).Where(x => x.Cat == "Reader"))
                _declarations.Add($"{asm.Name.Name}: type {type.FullName} generic constraint {c.Type.FullName}");
            foreach (var fd in type.Fields)
            foreach (var c in ClassifyDeep(fd.FieldType).Where(x => x.Cat == "Reader"))
                _declarations.Add($"{asm.Name.Name}: field {fd.FullName} of type {c.Type.FullName}");
            foreach (var pd in type.Properties)
            foreach (var c in ClassifyDeep(pd.PropertyType).Where(x => x.Cat == "Reader"))
                _declarations.Add($"{asm.Name.Name}: property {pd.FullName} of type {c.Type.FullName}");

            foreach (var method in type.Methods)
            {
                foreach (var c in ClassifyDeep(method.ReturnType).Where(x => x.Cat == "Reader"))
                    _declarations.Add($"{asm.Name.Name}: method {method.FullName} returns {c.Type.FullName}");
                foreach (var p in method.Parameters)
                foreach (var c in ClassifyDeep(p.ParameterType).Where(x => x.Cat == "Reader"))
                    _declarations.Add($"{asm.Name.Name}: method {method.FullName} parameter '{p.Name}' of type {c.Type.FullName}");
                foreach (var gp in method.GenericParameters)
                foreach (var con in gp.Constraints)
                foreach (var c in ClassifyDeep(con.ConstraintType).Where(x => x.Cat == "Reader"))
                    _declarations.Add($"{asm.Name.Name}: method {method.FullName} generic constraint {c.Type.FullName}");

                if (!method.HasBody) continue;
                MethodBody body;
                try { body = method.Body; }
                catch (Exception ex) { Unresolved.Add($"could not read body of {method.FullName}: {ex.Message}"); continue; }

                foreach (var v in body.Variables)
                foreach (var c in ClassifyDeep(v.VariableType).Where(x => x.Cat == "Reader"))
                    _declarations.Add($"{asm.Name.Name}: local V_{v.Index} of type {c.Type.FullName} in {method.FullName}");

                var ins = body.Instructions;
                for (int i = 0; i < ins.Count; i++)
                    ScanInstruction(f, asm, info, method, ins, i);
            }
        }
    }

    void ScanInstruction(AssetFile f, AssemblyDefinition asm, string info, MethodDefinition method, IList<Instruction> ins, int i)
    {
        var instr = ins[i];
        var code = instr.OpCode.Code;

        // obj.GetType().GetProperty("name") / GetMethod / GetField ...: name-based reflection on an object's
        // runtime type. On a decorator the member is missing (or different), so the code silently skips or throws.
        if (instr.Operand is MethodReference gt && gt.Name == "GetType" && gt.DeclaringType.FullName == "System.Object" && gt.Parameters.Count == 0)
        {
            string? name = null;
            for (int k = i + 1; k < Math.Min(ins.Count, i + 8); k++)
            {
                if (ins[k].OpCode.Code == Code.Ldstr) name ??= (string)ins[k].Operand;
                if (ins[k].Operand is MethodReference rm && ins[k].OpCode.FlowControl == FlowControl.Call &&
                    (rm.DeclaringType.FullName is "System.Type" or "System.Reflection.TypeInfo" or "System.Reflection.RuntimeReflectionExtensions" or "System.Reflection.TypeExtensions") &&
                    (rm.Name.StartsWith("Get") || rm.Name == "InvokeMember") && name != null)
                {
                    string ctx = string.Join(" | ", Enumerable.Range(Math.Max(0, i - 3), Math.Min(ins.Count, k + 2) - Math.Max(0, i - 3)).Select(x => Short(ins[x])));
                    RuntimeReflection.Add($"{asm.Name.Name} {asm.Name.Version}: {Logical(method)} IL_{instr.Offset:x4} GetType().{rm.Name}(\"{name}\")" +
                        Environment.NewLine + $"            ctx: {ctx}");
                    break;
                }
            }
        }

        if (instr.Operand is string s && code == Code.Ldstr)
        {
            if (s.Contains("DataReader", StringComparison.Ordinal))
                _strings.Add($"{asm.Name.Name}: {Logical(method)} IL_{instr.Offset:x4} ldstr \"{Trunc(s, 160)}\"");
            return;
        }

        switch (instr.Operand)
        {
            case TypeReference tr when (code is Code.Castclass or Code.Isinst or Code.Unbox_Any) && _extraInterfaces.ContainsKey(tr.FullName):
            {
                var e = _extraInterfaces[tr.FullName];
                Add(f, asm, info, method, ins, i, e.Cat + "Interface", "cast", tr.FullName, ScopeOf(tr),
                    $"interface not implemented by the System.Data.Common base; implemented by {string.Join(", ", e.Implementers.Take(3))}{(e.Implementers.Count > 3 ? " ..." : "")}", "dependency");
                break;
            }
            case MethodReference gm when gm.DeclaringType.FullName == "System.Data.Common.DbDataReaderExtensions":
            {
                Add(f, asm, info, method, ins, i, "ReaderInterface", "member", gm.FullName, ScopeOf(gm.DeclaringType),
                    "DbDataReaderExtensions checks for IDbColumnSchemaGenerator (throws NotSupportedException otherwise)", "dependency");
                break;
            }
            case TypeReference tr:
            {
                string kind = code switch
                {
                    Code.Castclass or Code.Isinst or Code.Unbox or Code.Unbox_Any => "cast",
                    Code.Ldtoken => "ldtoken",
                    _ => "typeop",
                };
                foreach (var c in ClassifyDeep(tr))
                {
                    // casts and ldtokens: every category; other type operands: readers only.
                    if (kind == "typeop" && c.Cat != "Reader") continue;
                    if (kind == "ldtoken" && c.Cat != "Reader" && c.Cat != "Command" && c.Cat != "Connection" && c.Cat != "Parameter") continue;
                    bool top = ReferenceEquals(c.Type, tr);
                    string role = kind switch
                    {
                        "typeop" when !top => "signature",          // initobj TaskAwaiter<SpannerDataReader>
                        "typeop" when IsOwn(asm, c.Type) => "own-type",
                        _ => "dependency",
                    };
                    Add(f, asm, info, method, ins, i, c.Cat, kind, tr.FullName, ScopeOf(c.Type),
                        (top ? "" : "generic argument: ") + c.Chain, role);
                }
                break;
            }
            case MethodReference mr:
            {
                string kind = code == Code.Ldtoken ? "ldtoken" : "member";
                var seen = new HashSet<string>();
                var declCats = ClassifyDeep(mr.DeclaringType).ToList();
                bool declaredOnReader = false;
                foreach (var c in declCats)
                {
                    if (c.Cat != "Reader" && kind != "ldtoken") continue; // members: readers only (task section c)
                    if (c.Cat != "Reader" && c.Cat != "Command" && c.Cat != "Connection" && c.Cat != "Parameter") continue;
                    bool top = ReferenceEquals(c.Type, mr.DeclaringType);
                    if (top && c.Cat == "Reader") declaredOnReader = true;
                    string role = !top ? "signature"                      // Task<SpannerDataReader>::GetAwaiter
                        : kind == "member" && IsOwn(asm, c.Type) ? "own-type" // the provider's own DbDataReader subclass
                        : "dependency";
                    if (seen.Add(c.Cat + "decl" + c.Type.FullName))
                        Add(f, asm, info, method, ins, i, c.Cat, kind, mr.FullName, ScopeOf(c.Type),
                            (top ? "declared on " : "generic argument of the declaring type: ") + c.Chain, role);
                }
                // Signature / generic arguments mentioning a reader type (e.g. NpgsqlCommand.ExecuteReader()).
                if (!declaredOnReader)
                {
                    var sigTypes = new List<TypeReference> { mr.ReturnType };
                    sigTypes.AddRange(mr.Parameters.Select(p => p.ParameterType));
                    if (mr is GenericInstanceMethod gim) sigTypes.AddRange(gim.GenericArguments);
                    foreach (var st in sigTypes)
                    foreach (var c in ClassifyDeep(st).Where(x => x.Cat == "Reader"))
                        if (seen.Add("sig" + c.Type.FullName))
                            Add(f, asm, info, method, ins, i, c.Cat, kind, mr.FullName, ScopeOf(c.Type), "signature mentions " + c.Chain, "signature");
                }
                break;
            }
            case FieldReference fr:
            {
                string kind = code == Code.Ldtoken ? "ldtoken" : "field";
                var seen = new HashSet<string>();
                foreach (var c in ClassifyDeep(fr.DeclaringType).Concat(ClassifyDeep(fr.FieldType)).Where(x => x.Cat == "Reader"))
                    if (seen.Add(c.Type.FullName))
                        Add(f, asm, info, method, ins, i, c.Cat, kind, fr.FullName, ScopeOf(c.Type), c.Chain,
                            kind == "ldtoken" ? "dependency" : "carry");
                break;
            }
        }
    }

    // The type is defined in the scanned assembly itself (e.g. Spanner's SpannerDataReaderWithChecksum,
    // EF Core's BufferedDataReader): calls to its members are the provider talking to its own object.
    static bool IsOwn(AssemblyDefinition asm, TypeReference? t)
    {
        if (t == null) return false;
        while (t is TypeSpecification spec && spec.ElementType != null) t = spec.ElementType;
        var td = SafeResolve(t);
        return td != null && td.Module.Assembly.Name.Name == asm.Name.Name;
    }

    // Roles:
    //   dependency  = depends on the object's concrete type: a cast, a typeof()/methodof() feeding reflection or
    //                 an expression tree, or a call to a member only the concrete type has;
    //   scaffolding = the same, but in design-time reverse-engineering code that runs raw ADO.NET (never sees
    //                 an interceptor's object);
    //   signature   = a call whose signature or generic arguments merely mention a concrete reader
    //                 (NpgsqlCommand.ExecuteReader(), Task<SpannerDataReader>.GetAwaiter());
    //   own-type    = a call to a member of a DbDataReader subclass the provider assembly itself defines;
    //   carry       = a load/store of a field typed as a concrete reader (compiler-generated state machine
    //                 fields) that only holds an already-cast value.
    void Add(AssetFile f, AssemblyDefinition asm, string info, MethodDefinition method, IList<Instruction> ins, int i,
        string cat, string kind, string operand, string operandAsm, string chain, string role)
    {
        var instr = ins[i];
        string nearby = kind == "ldtoken" ? Nearby(ins, i) : "";
        string context = string.Join(" | ", Enumerable.Range(i - 3, 7)
            .Where(k => k >= 0 && k < ins.Count)
            .Select(k => (k == i ? ">> " : "") + Short(ins[k])));
        string src = SourceOf(method, instr);
        if (src.Length > 0) context = src + " :: " + context;
        string logical = Logical(method);
        if (role == "dependency" && logical.Contains(".Scaffolding.", StringComparison.Ordinal)) role = "scaffolding";
        if (IsOwn(asm, ResolveOperandType(instr.Operand))) operandAsm += " (OWN TYPE of the scanned assembly)";
        Hits.Add(new Hit(Name, f.PackageId, f.PackageVersion, asm.Name.Name, asm.Name.Version.ToString(), info,
            cat, kind, method.DeclaringType.FullName, method.FullName, logical, instr.Offset,
            instr.OpCode.Name, operand, operandAsm, chain, nearby, context, role));
    }

    static TypeReference ResolveOperandType(object? operand) => operand switch
    {
        TypeReference t => t,
        MemberReference m => m.DeclaringType,
        _ => null!,
    };

    static string SourceOf(MethodDefinition method, Instruction instr)
    {
        try
        {
            if (!method.DebugInformation.HasSequencePoints) return "";
            var sp = method.DebugInformation.SequencePoints.Where(p => p.Offset <= instr.Offset && !p.IsHidden).LastOrDefault();
            return sp == null ? "" : $"{sp.Document.Url}:{sp.StartLine}";
        }
        catch { return ""; }
    }

    // What consumes an ldtoken: the next ldstr (method/property name) and the next call that is not
    // Type.GetTypeFromHandle (GetMethod, GetRuntimeMethod, Expression.Convert, ...).
    static string Nearby(IList<Instruction> ins, int i)
    {
        string? str = null, call = null;
        for (int k = i + 1; k < Math.Min(ins.Count, i + 24); k++)
        {
            var x = ins[k];
            if (str == null && x.OpCode.Code == Code.Ldstr) str = (string)x.Operand;
            if (call == null && x.Operand is MethodReference mr && x.OpCode.FlowControl == FlowControl.Call &&
                mr.Name is not ("GetTypeFromHandle" or "GetMethodFromHandle" or "GetFieldFromHandle" or "GetTypeInfo" or "AsType" or "Empty"))
            {
                call = $"{mr.DeclaringType.Name}.{mr.Name}";
                // stop at the consuming call; an ldstr after it belongs to something else
                break;
            }
        }
        var parts = new List<string>();
        if (str != null) parts.Add($"ldstr \"{Trunc(str, 80)}\"");
        if (call != null) parts.Add($"then {call}");
        return string.Join(", ", parts);
    }

    static string Short(Instruction x)
    {
        string op = x.Operand switch
        {
            null => "",
            MethodReference mr => $"{mr.DeclaringType.Name}::{mr.Name}",
            FieldReference fr => $"{fr.DeclaringType.Name}::{fr.Name}",
            TypeReference tr => tr.Name,
            string s => "\"" + Trunc(s, 40) + "\"",
            Instruction t => $"IL_{t.Offset:x4}",
            Instruction[] ts => string.Join(",", ts.Select(t => $"IL_{t.Offset:x4}")),
            VariableDefinition v => $"V_{v.Index}",
            ParameterDefinition p => p.Name,
            _ => x.Operand.ToString() ?? "",
        };
        return $"IL_{x.Offset:x4} {x.OpCode.Name} {op}".TrimEnd();
    }

    static readonly Regex LambdaName = new(@"^<([^>]*)>([a-z])__", RegexOptions.Compiled);
    static readonly Regex StateMachineName = new(@"^<([^>]+)>d__\d+", RegexOptions.Compiled);

    // Ns.Type.Method for compiler-generated lambdas, closures, local functions and state machines.
    static string Logical(MethodDefinition m)
    {
        var t = m.DeclaringType;
        string name = m.Name;
        string suffix = "";
        var lm = LambdaName.Match(name);
        if (lm.Success)
        {
            name = lm.Groups[1].Value;
            suffix = lm.Groups[2].Value switch { "b" => " [lambda]", "g" => " [local function]", _ => " [generated]" };
        }
        while (t.IsNested && t.Name.StartsWith("<"))
        {
            var sm = StateMachineName.Match(t.Name);
            if (sm.Success)
            {
                if (name == "MoveNext" || name.Length == 0) name = sm.Groups[1].Value;
                suffix = " [state machine]" + suffix;
            }
            t = t.DeclaringType;
        }
        return $"{t.FullName}.{name}{suffix}";
    }

    static string Trunc(string s, int n)
    {
        s = s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return s.Length <= n ? s : s[..n] + "...";
    }

    // ---------------------------------------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------------------------------------

    public string RenderReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("==========================================================================================");
        sb.AppendLine($"PROJECT {Name}");
        sb.AppendLine("==========================================================================================");
        sb.AppendLine("Assemblies scanned:");
        foreach (var (f, asm, info, symbols) in _scanned)
            sb.AppendLine($"    {asm.Name.Name} {asm.Name.Version}  package {f.PackageId} {f.PackageVersion}  info '{info}'{(symbols ? "  [symbols]" : "")}\n        {f.Path}");
        sb.AppendLine();

        void Section(string title, IEnumerable<Hit> hits, bool withContext)
        {
            var list = hits.ToList();
            sb.AppendLine($"--- {title}: {list.Count} hit(s)");
            foreach (var g in list.GroupBy(h => $"{h.AssemblyName} {h.AssemblyVersion} ({h.PackageId} {h.PackageVersion})"))
            {
                sb.AppendLine($"  [{g.Key}]");
                foreach (var h in g.OrderBy(h => h.ContainingType).ThenBy(h => h.Method).ThenBy(h => h.Offset))
                {
                    sb.AppendLine($"    {h.LogicalMethod}");
                    sb.AppendLine($"        in   {h.Method}");
                    sb.AppendLine($"        IL_{h.Offset:x4}  {h.OpCode}  {h.Operand}   [{h.OperandAssembly}]  ({h.Category}: {h.Chain})");
                    if (h.Nearby.Length > 0) sb.AppendLine($"        near {h.Nearby}");
                    if (withContext) sb.AppendLine($"        ctx  {h.Context}");
                }
            }
            sb.AppendLine();
        }

        var readers = Hits.Where(h => h.Category == "Reader").ToList();
        Section("A. READER casts (castclass/isinst/unbox/unbox.any)", readers.Where(h => h.Kind == "cast"), true);
        Section("B. READER ldtoken (typeof / methodof / fieldof)", readers.Where(h => h.Kind == "ldtoken"), true);
        Section("C. READER member references (call/callvirt/newobj/ldftn) and field access", readers.Where(h => h.Kind is "member" or "field"), true);
        Section("C2. READER as other type operand (newarr/box/constrained./initobj...)", readers.Where(h => h.Kind == "typeop"), true);
        Section("E. OTHER ADO.NET concrete-type casts (Command/Connection/Parameter/Transaction/Batch/...)",
            Hits.Where(h => h.Category != "Reader" && h.Kind == "cast"), true);
        Section("G. Casts to INTERFACES that concrete ADO.NET types implement but their System.Data.Common base does not; DbDataReaderExtensions calls",
            Hits.Where(h => h.Category.EndsWith("Interface")), true);
        sb.AppendLine("--- G0. Interface inventory:");
        foreach (var n in ExtraInterfaceNotes) sb.AppendLine("    " + n);
        sb.AppendLine();
        Section("F. OTHER ADO.NET concrete-type ldtoken (Command/Connection/Parameter)",
            Hits.Where(h => h.Category != "Reader" && h.Kind == "ldtoken"), false);

        sb.AppendLine($"--- D1. Declarations using a reader type: {_declarations.Count}");
        foreach (var d in _declarations.Distinct()) sb.AppendLine("    " + d);
        sb.AppendLine();
        sb.AppendLine($"--- H. Name-based reflection on a runtime type (obj.GetType().GetProperty(\"x\") ...): {RuntimeReflection.Count}");
        foreach (var d in RuntimeReflection.Distinct()) sb.AppendLine("    " + d);
        sb.AppendLine();
        sb.AppendLine($"--- D2. ldstr literals containing 'DataReader': {_strings.Count}");
        foreach (var d in _strings.Distinct()) sb.AppendLine("    " + d);
        sb.AppendLine();
        sb.AppendLine("--- Clean assemblies (no reader cast, ldtoken, member reference or declaration):");
        foreach (var c in _clean) sb.AppendLine("    " + c);
        sb.AppendLine();
        if (Unresolved.Count > 0)
        {
            sb.AppendLine("--- Unresolved (could not be classified):");
            foreach (var u in Unresolved) sb.AppendLine("    " + u);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

// Resolves assemblies from the restored package graph (runtime assets first, then RID-specific
// runtime targets, then compile/ref assets) and falls back to the shared framework.
sealed class MapResolver : IAssemblyResolver
{
    readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, AssemblyDefinition> _cache = new(StringComparer.OrdinalIgnoreCase);

    public MapResolver(List<AssetFile> files, string sharedFramework)
    {
        int Rank(AssetFile f) => f.Kind switch
        {
            "runtime" => 0,
            var k when k.StartsWith("runtimeTarget:win") => 1,
            var k when k.StartsWith("runtimeTarget:") => 2,
            _ => 3,
        };
        foreach (var f in files.OrderBy(Rank))
        {
            var name = Path.GetFileNameWithoutExtension(f.Path);
            _map.TryAdd(name, f.Path);
        }
        foreach (var dll in Directory.GetFiles(sharedFramework, "*.dll"))
            _map.TryAdd(Path.GetFileNameWithoutExtension(dll), dll);
    }

    public AssemblyDefinition? Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters());

    public AssemblyDefinition? Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        if (_cache.TryGetValue(name.Name, out var a)) return a;
        if (!_map.TryGetValue(name.Name, out var path)) return null;
        try
        {
            a = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = this, ReadingMode = ReadingMode.Deferred });
        }
        catch
        {
            return null;
        }
        _cache[name.Name] = a;
        return a;
    }

    public void Dispose()
    {
        foreach (var a in _cache.Values) a.Dispose();
        _cache.Clear();
    }
}
