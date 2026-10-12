using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.Kafka.BuildInterception;
using Kronikol.Reports;
using static Kronikol.Tests.Kafka.BuildInterception.BuildInterceptionTestSupport;

namespace Kronikol.Tests.Kafka.BuildInterception;

// F2 (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 4.1): under tiered compilation the runtime re-JITs Build() after a
// few dozen calls and the detour on shared generic code is lost for good. Nothing in process holds it; quick JIT off does.
// The interceptor reads the settings the way the runtime does and says so when the patch can be lost.
public class KafkaBuildInterceptorTierTests : IDisposable
{
    public void Dispose() => KafkaBuildInterceptor.Reset();

    private static KafkaTrackingOptions MakeOptions() => new()
    {
        CurrentTestInfoFetcher = () => ("BuildInterception Tier Test", "tier-test"),
    };

    [Fact]
    public void With_the_test_projects_quick_JIT_off_four_hundred_builds_are_all_tracked()
    {
        // This project's <TieredCompilationQuickJit>false</TieredCompilationQuickJit>, as the runtime was handed it.
        Assert.Equal("false", AppContext.GetData("System.Runtime.TieredCompilation.QuickJit")?.ToString());
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());

        var untracked = 0;
        for (var i = 0; i < 400; i++)
        {
            using var producer = new ProducerBuilder<string, string>(ProducerConfig()).Build();
            if (producer is not TrackingKafkaProducer<string, string>) untracked++;
        }

        Assert.Equal(0, untracked);
    }

    // The runtime's own reading, measured case by case (plan harness audit/B/e5): DOTNET_ before COMPlus_, each read as a
    // hexadecimal number from its leading digits (no digits: unset), and only then the runtimeconfig value.
    public static TheoryData<string, string?, string?, bool> Settings => new()
    {
        { "", null, null, true },                                                    // the default: quick JIT on
        { "", null, "false", false },                                                // the csproj property
        { "DOTNET_TC_QuickJit=0", null, null, false },
        { "DOTNET_TieredCompilation=0", null, null, false },
        { "DOTNET_TC_QuickJit=1", null, "false", true },                             // the environment beats the csproj
        { "DOTNET_TC_QuickJit=false", null, null, true },                            // "fa" is 0xFA: on
        { "COMPlus_TC_QuickJit=0", null, null, false },
        { "", "false", null, false },                                                // csproj TieredCompilation=false
        { "DOTNET_TieredCompilation=1", "false", null, true },
        { "DOTNET_TC_QuickJit=false", null, "false", true },                         // 0xFA beats the csproj too
        { "DOTNET_TC_QuickJit=true", null, null, true },                             // no hex digits: ignored
        { "DOTNET_TC_QuickJit=true", null, "false", false },
        { "DOTNET_TC_QuickJit=zzz", null, "false", false },
        { "DOTNET_TC_QuickJit=0x0", null, null, false },
        { "DOTNET_TC_QuickJit=00", null, null, false },
        { "DOTNET_TieredCompilation=false", null, null, true },                      // tiering stays on
        { "DOTNET_TC_QuickJit=1;COMPlus_TC_QuickJit=0", null, null, true },          // DOTNET_ beats COMPlus_
        { "COMPlus_TieredCompilation=0", null, null, false },
    };

    [Theory]
    [MemberData(nameof(Settings))]
    public void The_check_reads_the_tiering_settings_as_the_runtime_does(string environment, string? tieredCompilation, string? quickJit, bool canLoseThePatch)
    {
        var check = Check(environment, tieredCompilation, quickJit, new StringWriter());

        Assert.Equal(canLoseThePatch, check.CanLoseThePatch);
    }

    [Fact]
    public void Enabling_where_tiering_can_lose_the_patch_writes_one_line_and_records_one_diagnostic_naming_the_remedy()
    {
        var standardError = new StringWriter();
        var processCheck = KafkaBuildInterceptor.TieringCheck;
        KafkaBuildInterceptor.TieringCheck = Check("DOTNET_TC_QuickJit=1", null, null, standardError);
        try
        {
            KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
            KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

            var line = Assert.Single(standardError.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            Assert.Contains("<TieredCompilationQuickJit>false</TieredCompilationQuickJit>", line);
            var entry = Assert.Single(ProcessDiagnostics.Entries, e => e.Message == TieredCompilationCheck.Message);
            Assert.Equal(DiagnosticKind.CaptureDegraded, entry.Kind);
            Assert.Contains("<TieredCompilationQuickJit>false</TieredCompilationQuickJit>", entry.Message);
        }
        finally
        {
            KafkaBuildInterceptor.TieringCheck = processCheck;
            ProcessDiagnostics.Remove(TieredCompilationCheck.DiagnosticKey);
        }
    }

    // A suite that only produces, or only consumes, enables one direction: each must say so on its own.
    [Theory]
    [InlineData("producer")]
    [InlineData("consumer")]
    public void Enabling_one_direction_alone_where_tiering_can_lose_the_patch_says_so(string direction)
    {
        var standardError = new StringWriter();
        var processCheck = KafkaBuildInterceptor.TieringCheck;
        KafkaBuildInterceptor.TieringCheck = Check("DOTNET_TC_QuickJit=1", null, null, standardError);
        try
        {
            if (direction == "producer")
                KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
            else
                KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

            Assert.Contains("<TieredCompilationQuickJit>false</TieredCompilationQuickJit>", standardError.ToString());
            Assert.Single(ProcessDiagnostics.Entries, e => e.Message == TieredCompilationCheck.Message);
        }
        finally
        {
            KafkaBuildInterceptor.TieringCheck = processCheck;
            ProcessDiagnostics.Remove(TieredCompilationCheck.DiagnosticKey);
        }
    }

    [Fact]
    public void Enabling_under_this_projects_setting_writes_and_records_nothing()
    {
        var standardError = new StringWriter();
        var previous = Console.Error;
        Console.SetError(standardError);
        try
        {
            KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        }
        finally
        {
            Console.SetError(previous);
        }

        Assert.Equal("", standardError.ToString());
        Assert.DoesNotContain(ProcessDiagnostics.Entries, e => e.Message == TieredCompilationCheck.Message);
    }

    private static TieredCompilationCheck Check(string environment, string? tieredCompilation, string? quickJit, TextWriter standardError)
    {
        var variables = environment.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1]);
        return new TieredCompilationCheck(
            name => variables.GetValueOrDefault(name),
            name => name switch
            {
                "System.Runtime.TieredCompilation" => tieredCompilation,
                "System.Runtime.TieredCompilation.QuickJit" => quickJit,
                _ => null,
            },
            () => standardError);
    }
}
