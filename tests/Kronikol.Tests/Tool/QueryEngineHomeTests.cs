using System.Reflection;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The query engine lives in the <c>Kronikol</c> package, not in the tool (roadmap stage 1b,
/// <c>plans/QUERY_FALLBACK_PLAN.md</c> M1).
///
/// <para>That is what lets a report carry its own way of being queried: every test project that writes a
/// report has already restored <c>Kronikol</c>, at the exact version that wrote it, so the
/// <c>query.cs</c> written beside the report can run the engine with nothing installed and nothing fetched.
/// The tool keeps <c>kronikol query</c> by calling the same type, so there is one engine and parity is by
/// construction.</para>
///
/// <para>Moving 9,484 lines into a library that consumers reference would also have turned every
/// <c>public</c> member of those files into public API, so the surface is pinned here: one type,
/// two methods.</para>
/// </summary>
public class QueryEngineHomeTests
{
    private static readonly Assembly Library = typeof(ReportConfigurationOptions).Assembly;

    [Fact]
    public void The_engine_is_in_the_Kronikol_assembly_and_the_tool_keeps_no_copy()
    {
        var engine = Library.GetType("Kronikol.Query.QueryCommand");

        Assert.NotNull(engine);
        Assert.True(engine!.IsPublic, "Kronikol.Query.QueryCommand is not public");

        var tool = typeof(global::Kronikol.Tool.Commands).Assembly;
        Assert.Null(tool.GetType("Kronikol.Tool.QueryCommand"));
        Assert.Null(tool.GetType("Kronikol.Query.QueryCommand"));
        Assert.Empty(tool.GetTypes().Where(t => t.Namespace is "Kronikol.Tool.Query"));
    }

    [Fact]
    public void QueryCommand_is_the_only_public_type_the_engine_brings()
    {
        var exported = Library.GetExportedTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("Kronikol.Query", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToArray();

        Assert.Equal(["Kronikol.Query.QueryCommand"], exported);
    }

    [Fact]
    public void Its_public_surface_is_Run_and_PrintUsage()
    {
        var engine = Library.GetType("Kronikol.Query.QueryCommand", throwOnError: true)!;

        var members = engine
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(Describe)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Int32 Run(IReadOnlyList`1, TextWriter, TextWriter)", "Void PrintUsage(TextWriter)"],
            members);
    }

    private static string Describe(MemberInfo member) => member is MethodInfo method
        ? $"{method.ReturnType.Name} {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})"
        : $"{member.MemberType} {member.Name}";
}
