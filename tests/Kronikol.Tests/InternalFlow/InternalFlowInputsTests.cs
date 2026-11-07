using System.Reflection;
using Kronikol.InternalFlow;
using Kronikol.Tracking;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// #138: four inputs that never changed what was captured (<c>plans/INTERNAL_FLOW_SOURCES_PLAN.md</c> R4, F6). The listener
/// hears every source but the runtime's connection-level ones, so the source names given to the handler's options, to the
/// listener's constructor and to <c>AddActivityListenerForInternalFlowTracking</c> were never read. They are obsolete, with
/// overloads that take none; <c>ReportConfigurationOptions.InternalFlowActivitySources</c> with <c>Manual</c> filters.
/// </summary>
public class InternalFlowInputsTests
{
    public static TheoryData<string> ObsoleteInputs =>
    [
        "TestTrackingMessageHandlerOptions.InternalFlowActivitySources",
        "InternalFlowActivityListener(params string[])",
        "AddActivityListenerForInternalFlowTracking(IServiceCollection, params string[])",
    ];

    [Theory]
    [MemberData(nameof(ObsoleteInputs))]
    public void An_input_that_was_never_read_is_obsolete_and_says_what_filters(string input)
    {
        var obsolete = Member(input).GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete.IsError);
        Assert.Contains("ReportConfigurationOptions.InternalFlowActivitySources", obsolete.Message);
        Assert.Contains("Manual", obsolete.Message);
    }

    [Fact]
    public void The_overloads_with_no_names_are_not_obsolete()
    {
        Assert.Null(typeof(InternalFlowActivityListener).GetConstructor(Type.EmptyTypes)!.GetCustomAttribute<ObsoleteAttribute>());
        Assert.Null(typeof(InternalFlowServiceCollectionExtensions)
            .GetMethod(nameof(InternalFlowServiceCollectionExtensions.AddActivityListenerForInternalFlowTracking), [typeof(IServiceCollection)])!
            .GetCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void A_call_with_no_names_compiles_without_a_warning_and_one_with_names_warns()
    {
        // Overload resolution takes the overload without params for a call with no arguments, so only a caller that passes
        // names, or sets the option, is told.
        const string withoutNames = """
            using Kronikol.InternalFlow;
            using Microsoft.Extensions.DependencyInjection;
            static class Calls
            {
                static void Run(IServiceCollection services)
                {
                    using var listener = new InternalFlowActivityListener();
                    services.AddActivityListenerForInternalFlowTracking();
                    _ = new Kronikol.Tracking.TestTrackingMessageHandlerOptions { CallerName = "Tests" };
                }
            }
            """;
        const string withNames = """
            using Kronikol.InternalFlow;
            using Microsoft.Extensions.DependencyInjection;
            static class Calls
            {
                static void Run(IServiceCollection services)
                {
                    using var listener = new InternalFlowActivityListener("MyApp");
                    services.AddActivityListenerForInternalFlowTracking("MyApp");
                    _ = new Kronikol.Tracking.TestTrackingMessageHandlerOptions { InternalFlowActivitySources = ["MyApp"] };
                }
            }
            """;

        Assert.Empty(Obsolete(withoutNames));
        Assert.Equal(3, Obsolete(withNames).Length);
    }

    private static MemberInfo Member(string input) => input switch
    {
        "TestTrackingMessageHandlerOptions.InternalFlowActivitySources" =>
            typeof(TestTrackingMessageHandlerOptions).GetProperty("InternalFlowActivitySources")!,
        "InternalFlowActivityListener(params string[])" =>
            typeof(InternalFlowActivityListener).GetConstructor([typeof(string[])])!,
        _ => typeof(InternalFlowServiceCollectionExtensions)
            .GetMethod(nameof(InternalFlowServiceCollectionExtensions.AddActivityListenerForInternalFlowTracking),
                [typeof(IServiceCollection), typeof(string[])])!,
    };

    /// <summary>The CS0618 warnings a compile of <paramref name="source"/> against Kronikol raises.</summary>
    private static Diagnostic[] Obsolete(string source)
    {
        // The app's own assemblies, Kronikol and its dependencies among them, as the step weaver's tests compile.
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator)
            .Append(typeof(InternalFlowActivityListener).Assembly.Location)
            .Append(typeof(IServiceCollection).Assembly.Location)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ObsoleteInputs", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var diagnostics = compilation.GetDiagnostics();
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return diagnostics.Where(d => d.Id == "CS0618").ToArray();
    }
}
