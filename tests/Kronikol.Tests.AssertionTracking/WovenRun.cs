using System.Reflection;
using Kronikol.AssertionTracking;
using Kronikol.Tracking;
using Microsoft.CodeAnalysis;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// Builds a fixture, weaves it, runs one of its methods under a test identity of its own, and reads back the
/// assertion notes that run drew, so a fact can compare what a reader sees rather than the IL.
/// </summary>
internal static class WovenRun
{
    /// <summary>One assertion note: its outcome, its label, the failure message under it and its source comment.</summary>
    public sealed record Note(bool Passed, string Label, string? Message, string? Source);

    public static string BuildAndWeave(
        string name, string source, OptimizationLevel optimization = OptimizationLevel.Debug, int? woven = null) =>
        BuildAndWeave(name, source, optimization, [], woven);

    public static string BuildAndWeave(
        string name, string source, OptimizationLevel optimization, IReadOnlyList<string> extraReferences, int? woven = null)
    {
        var assemblyPath = TestAssemblyBuilder.Build(name, source, optimization, Microsoft.CodeAnalysis.Emit.DebugInformationFormat.PortablePdb, extraReferences);
        var result = new AssertionWeaver().Weave(assemblyPath, Path.ChangeExtension(assemblyPath, ".pdb"));
        if (woven is { } expected)
            Assert.Equal(expected, result.WeavedCount);
        return assemblyPath;
    }

    public static (Exception? Thrown, Note[] Notes) Run(string assemblyPath, string typeName, string methodName)
    {
        var type = Assembly.LoadFrom(assemblyPath).GetType(typeName)!;
        var method = type.GetMethod(methodName)!;
        var instance = method.IsStatic ? null : Activator.CreateInstance(type);

        var testId = $"{methodName}_{Guid.NewGuid():N}";
        Exception? thrown;
        using (TestIdentityScope.Begin(testId, testId))
        {
            thrown = Record.Exception(() =>
            {
                if (method.Invoke(instance, null) is Task task)
                    task.GetAwaiter().GetResult();
            });
        }

        if (thrown is TargetInvocationException { InnerException: { } inner })
            thrown = inner;
        return (thrown, NotesFor(testId));
    }

    public static Note[] NotesFor(string testId) =>
        RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml is not null && l.PlantUml.Contains("<<assertionNote>>"))
            .Select(l => Parse(l.PlantUml!))
            .ToArray();

    // hnote across <<assertionNote>> #colour / "✓ label" or "✗ label" and the message / end note / '__^*__:file:Lline
    private static Note Parse(string plantUml)
    {
        var lines = plantUml.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("hnote across <<assertionNote>>", StringComparison.Ordinal));
        var end = Array.IndexOf(lines, "end note", start);
        var body = lines[(start + 1)..end];
        var message = body.Length > 1 ? string.Join("\n", body[1..]) : null;
        var source = lines.Skip(end + 1).FirstOrDefault(l => l.StartsWith("'__^*__:", StringComparison.Ordinal));
        return new Note(body[0].StartsWith('✓'), body[0][2..], message, source?["'__^*__:".Length..]);
    }
}
