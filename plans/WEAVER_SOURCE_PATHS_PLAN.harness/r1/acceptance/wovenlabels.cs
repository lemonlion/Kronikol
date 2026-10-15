#:package Mono.Cecil@0.11.6
// What the weave wrote into an assembly, for comparing two builds of one source (WEAVER_SOURCE_PATHS_PLAN section 5.4):
// the number of woven statements (calls to Track.AssertionPassed*), the fallback labels ("assertion at line N"), and
// every string literal of every method that calls Track.Assertion*, the file paths left out: the labels and the names
// of the values captured. Two builds whose labels and values are the same print the same lines.
// Usage: dotnet run --file wovenlabels.cs -- <assembly>
using Mono.Cecil;
using Mono.Cecil.Cil;

var path = args[0];
using var module = ModuleDefinition.ReadModule(path);
var literals = new List<string>();
var woven = 0;
var fallback = 0;
foreach (var type in module.GetTypes())
{
    foreach (var method in type.Methods.Where(m => m.HasBody))
    {
        var instructions = method.Body.Instructions;
        if (!instructions.Any(i => i.Operand is MethodReference r && IsTrack(r)))
            continue;
        foreach (var instruction in instructions)
        {
            if (instruction.Operand is MethodReference called && IsTrack(called) && called.Name.StartsWith("AssertionPassed", StringComparison.Ordinal))
                woven++;
            if (instruction.OpCode != OpCodes.Ldstr || instruction.Operand is not string literal)
                continue;
            if (literal.StartsWith("assertion at line", StringComparison.Ordinal))
                fallback++;
            if (literal.EndsWith(".cs", StringComparison.Ordinal) || literal.EndsWith(".feature", StringComparison.Ordinal))
                continue;
            literals.Add($"{type.FullName}::{method.Name}: {literal.ReplaceLineEndings(" ")}");
        }
    }
}

Console.WriteLine($"woven {woven}, fallback labels {fallback}, literals {literals.Count}");
foreach (var line in literals.Order(StringComparer.Ordinal))
    Console.WriteLine(line);

static bool IsTrack(MethodReference method) =>
    method.DeclaringType.FullName == "Kronikol.Tracking.Track" && method.Name.StartsWith("Assertion", StringComparison.Ordinal);
