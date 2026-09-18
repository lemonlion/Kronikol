using System.Reflection;
using System.Runtime.CompilerServices;
using LightBDD.Core.Configuration;
using LightBDD.Framework.Configuration;

namespace Kronikol.LightBDD.xUnit2;

/// <summary>
/// Provides xUnit v2-specific extension methods for configuring LightBDD report writers with test tracking diagram generation.
/// </summary>
public static class ReportWritersConfigurationExtensions
{
    /// <summary>
    /// Configures LightBDD to generate test tracking diagrams and reports.
    /// This overload operates on <see cref="ReportWritersConfiguration"/> only and does NOT register
    /// the automatic argument capture decorator.
    /// Prefer the <see cref="LightBddConfiguration"/> overload for automatic raw argument capture.
    /// </summary>
    [Obsolete("Use the LightBddConfiguration.CreateStandardReportsWithDiagrams() overload instead, which also registers automatic argument capture for rich rendering of complex parameters.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReportWritersConfiguration CreateStandardReportsWithDiagrams(
        this ReportWritersConfiguration configuration,
        ReportConfigurationOptions options)
    {
        return LightBDD.ReportWritersConfigurationExtensions.CreateStandardReportsWithDiagramsInternal(
            configuration, options, Assembly.GetCallingAssembly(),
            assembly => assembly.CountNumberOfTestsInAssembly());
    }

    /// <summary>
    /// Configures LightBDD to generate test tracking diagrams and reports with automatic raw argument capture.
    /// This overload registers an <see cref="global::LightBDD.Core.Extensibility.Execution.IScenarioDecorator"/> that captures
    /// raw test method arguments during scenario execution, enabling rich sub-table and expandable rendering
    /// for complex objects (records, lists, nested types) passed via framework-level attributes (MemberData,
    /// ClassData, InlineData, etc.) — using the same processing pipeline as the non-LightBDD xUnit2 adapter.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LightBddConfiguration CreateStandardReportsWithDiagrams(
        this LightBddConfiguration configuration,
        ReportConfigurationOptions options)
    {
        return LightBDD.ReportWritersConfigurationExtensions.CreateStandardReportsWithDiagramsInternal(
            configuration, options, Assembly.GetCallingAssembly(),
            assembly => assembly.CountNumberOfTestsInAssembly());
    }
}