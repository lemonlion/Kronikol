using System.Reflection;
using System.Runtime.CompilerServices;
using LightBDD.Core.Configuration;
using LightBDD.Core.ExecutionContext;
using LightBDD.Framework.Configuration;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.LightBDD
{
    /// <summary>
    /// Provides extension methods for configuring LightBDD report writers to generate test tracking diagrams and reports.
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
            ReportConfigurationOptions options,
            Func<Assembly, int> testCountResolver)
        {
            return CreateStandardReportsWithDiagramsInternal(configuration, options, Assembly.GetCallingAssembly(), testCountResolver);
        }

        /// <summary>
        /// Configures LightBDD to generate test tracking diagrams and reports with automatic raw argument capture.
        /// This overload registers an <see cref="global::LightBDD.Core.Extensibility.Execution.IScenarioDecorator"/> that captures
        /// raw test method arguments during scenario execution, enabling rich sub-table and expandable rendering
        /// for complex objects (records, lists, nested types) passed via framework-level attributes (MemberData, ClassData,
        /// TestCase, TestCaseSource, etc.) — using the same processing pipeline as the non-LightBDD adapters.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LightBddConfiguration CreateStandardReportsWithDiagrams(
            this LightBddConfiguration configuration,
            ReportConfigurationOptions options,
            Func<Assembly, int> testCountResolver)
        {
            return CreateStandardReportsWithDiagramsInternal(
                configuration, options, Assembly.GetCallingAssembly(), testCountResolver);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static LightBddConfiguration CreateStandardReportsWithDiagramsInternal(
            LightBddConfiguration configuration, ReportConfigurationOptions options, Assembly testAssembly,
            Func<Assembly, int> testCountResolver, bool registerDefaultDecorator = true)
        {
            if (registerDefaultDecorator)
            {
                configuration.ExecutionExtensionsConfiguration()
                    .EnableScenarioDecorator<ArgumentCaptureScenarioDecorator>();
            }

            configuration.ExecutionExtensionsConfiguration()
                .EnableStepDecorator<StepTrackingStepDecorator>();

            CreateStandardReportsWithDiagramsInternal(
                configuration.ReportWritersConfiguration(), options, testAssembly, testCountResolver);

            return configuration;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static ReportWritersConfiguration CreateStandardReportsWithDiagramsInternal(
            ReportWritersConfiguration configuration, ReportConfigurationOptions options, Assembly testAssembly, Func<Assembly, int> testCountResolver)
        {
            // Enable Track.That assertions to resolve the current LightBDD scenario ID.
            Track.TestIdResolver ??= () => ScenarioExecutionContext.CurrentScenario.Info.RuntimeId.ToString();

            // Disable LightBDD's built-in report writers and register a single formatter
            // that delegates to the standard ReportGenerator pipeline — the same pipeline
            // used by every other framework adapter (xUnit, NUnit, MSTest, TUnit, BDDfy, ReqNRoll).
            var reportsFilePath = options.ReportsFolderPath.Trim().TrimEnd('/');

            return configuration
                .Clear()
                .AddFileWriter<StandardPipelineFormatter>($"{reportsFilePath}/.generation-complete",
                formatter =>
                {
                    options.ExpectedTestCount = () => testCountResolver(testAssembly);
                    formatter.Options = options;
                });
        }
    }
}