#:package System.Reflection.MetadataLoadContext@9.0.0
#:property LangVersion=latest
// Usage: dotnet run --file api.cs -- <dll> [more search dirs...]  : prints the public and protected surface of one assembly, one member a line.
using System.Reflection;
using System.Runtime.InteropServices;

var dll = args[0];
var dirs = new List<string> { Path.GetDirectoryName(dll)!, RuntimeEnvironment.GetRuntimeDirectory() };
var shared = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "Microsoft.AspNetCore.App"));
if (Directory.Exists(shared)) dirs.Add(Directory.GetDirectories(shared).OrderBy(x => x).Last());
dirs.AddRange(args.Skip(1));
var files = dirs.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.dll")).ToList();
files.Add(dll);
var resolver = new PathAssemblyResolver(files.Distinct());
using var mlc = new MetadataLoadContext(resolver, "System.Runtime");
var asm = mlc.LoadFromAssemblyPath(dll);
var lines = new List<string>();
const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
Type[] types;
try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t is not null).ToArray()!; }
foreach (var t in types.Where(t => t.IsPublic || t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamORAssem))
{
    lines.Add($"T {t.FullName}");
    MemberInfo[] members;
    try { members = t.GetMembers(all); } catch { lines.Add($"? {t.FullName} members unreadable"); continue; }
    foreach (var m in members)
    {
        try
        {
            switch (m)
            {
                case MethodBase b when b.IsPublic || b.IsFamily || b.IsFamilyOrAssembly:
                    var returns = b is MethodInfo mi ? mi.ReturnType.ToString() : "";
                    lines.Add($"M {t.FullName}::{b.Name}({string.Join(", ", b.GetParameters().Select(p => $"{p.ParameterType} {p.Name}{(p.HasDefaultValue ? " = default" : "")}"))}) : {returns}");
                    break;
                case FieldInfo f when f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly:
                    lines.Add($"F {t.FullName}::{f.Name} : {f.FieldType}");
                    break;
            }
        }
        catch (Exception e) { lines.Add($"? {t.FullName}::{m.Name} {e.GetType().Name}"); }
    }
}
lines.Sort(StringComparer.Ordinal);
foreach (var l in lines) Console.WriteLine(l);
