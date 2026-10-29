using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Mono.Cecil;

namespace Kronikol.Tests.AssertionTracking;

// Probe for plans/AWAITED_ASSERTION_VALUES_PLAN.md (not a fact to keep): does csc's /debug+ (DebugPlusMode) decide
// whether an optimized build keeps the issue's closure in a local of MoveNext?
public class Probe211DebugPlusTests
{
    private const string Source = """
        using System;
        using System.Threading.Tasks;

        public sealed class Request { public string Id { get; init; } = ""; }
        public sealed class Client { public async Task<string> CallAsync(string id) { await Task.Yield(); throw new InvalidOperationException(id); } }

        public class Cases
        {
            private readonly Client _client = new();

            public async Task Issue_Unassigned()
            {
                var request = new Request { Id = string.Empty };
                await Task.WhenAll(Run(() => _client.CallAsync(request.Id)));
            }

            public async Task Short_closure()
            {
                var id = "a1";
                await Task.WhenAll(Run(() => _client.CallAsync(id)));
            }

            private static Task Run(Func<Task> f) => Task.CompletedTask;
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Closure_local_by_debug_plus_mode(bool debugPlus)
    {
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithOptimizationLevel(OptimizationLevel.Release);
        if (debugPlus)
            options = (CSharpCompilationOptions)typeof(CSharpCompilationOptions)
                .GetMethod("WithDebugPlusMode", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
                .Invoke(options, [true])!;
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location));
        var compilation = CSharpCompilation.Create($"Probe211_{debugPlus}", [CSharpSyntaxTree.ParseText(Source)], references, options);
        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var emitted = compilation.Emit(pe, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        pe.Position = 0;
        using var module = ModuleDefinition.ReadModule(pe);
        var lines = new List<string>();
        foreach (var type in module.GetTypes().Where(t => t.Name.Contains("d__")))
        {
            var moveNext = type.Methods.Single(m => m.Name == "MoveNext");
            var locals = string.Join(", ", moveNext.Body.Variables.Select(v => v.VariableType.Name));
            lines.Add($"{type.Name} debugPlus={debugPlus}: locals [{locals}]");
        }
        File.AppendAllLines(Environment.GetEnvironmentVariable("KRONIKOL_211_PROBE")!, lines);
    }
}
