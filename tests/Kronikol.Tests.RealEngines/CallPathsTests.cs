using Kronikol.Query;

namespace Kronikol.Tests.RealEngines;

/// <summary>
/// A call reads the deepest step it was made in, on each framework's real engine (#225): a Reqnroll [Step], a LightBDD step's
/// [Step] and a plain lane's sub-step give the same shape of path.
/// </summary>
public class CallPathsTests
{
    public static TheoryData<string> ReqnrollLanes => new() { "Engine.Reqnroll.xUnit2", "Engine.Reqnroll.xUnit3", "Engine.Reqnroll.TUnit", "Engine.Reqnroll.Floor" };

    [Theory, MemberData(nameof(ReqnrollLanes))]
    public void A_call_in_a_Reqnroll_step_method_reads_it(string lane) =>
        Assert.Equal("0.0 /customers, 1.0 /orders, 2.0 /orders/1", EngineLane.Run(lane).Rows.CallsOf("T00"));

    // LightBDD's own timer can read a scenario's last calls out of it, by a margin that varies from run to run, until #223's R3 (its F14).
    // So the first must be listed, and each call listed must read its step, whichever calls are listed.
    private static void ReadsItsCalls(string scenario, params string[] calls)
    {
        var read = EngineLane.Run("Engine.LightBDD").Rows.CallsOf(scenario);

        Assert.StartsWith(calls[0], read);
        foreach (var call in read.Split(", "))
            Assert.Contains(call, calls);
    }

    [Fact]
    public void A_call_in_a_LightBDD_steps_step_method_gives_the_same_path() =>
        ReadsItsCalls("L0", "0.0 /customers", "1.0 /orders", "2.0 /orders/1");

    [Fact]
    public void A_call_in_a_plain_lanes_sub_step_reads_it() =>
        Assert.Equal("0 /own, 0.0 /slow, 1 /orders", EngineLane.Run("Engine.xUnit3").Rows.CallsOf("P7"));

    [Theory, MemberData(nameof(ReqnrollLanes))]
    public void Two_step_methods_in_parallel_each_read_their_own(string lane)
    {
        var calls = EngineLane.Run(lane).Rows.CallsOf("T19");

        Assert.Contains("0.0 /first", calls);
        Assert.Contains("0.1 /second", calls);
    }

    // Paired by position, the assertion's row took the place of the first sub-step, and each call read the row above its own.
    [Fact]
    public void An_assertion_in_a_LightBDD_composites_own_method_leaves_each_call_its_own_sub_step() =>
        ReadsItsCalls("L6", "0.1.0 /customers", "0.2.0 /orders", "1.0 /orders/1");

    // The address a stamp gets and the one a reader looks up cannot drift, and a scenario's Background rows are its own.
    [Theory, MemberData(nameof(ReqnrollLanes))]
    public void Every_path_a_call_reads_is_a_step_the_report_lists(string lane)
    {
        using var index = ReportScanner.Scan(DataFile(lane));

        var read = index.Scenarios.SelectMany(scenario => scenario.Interactions.Where(call => call.StepPath is not null).Select(call => (scenario, call.StepPath))).ToArray();

        Assert.Empty(read.Where(call => call.scenario.AllSteps().All(step => step.Path != call.StepPath)).Select(call => $"{call.scenario.Name}: {call.StepPath}"));
        Assert.Contains(read, call => call.StepPath!.StartsWith('b'));
        Assert.Contains(read, call => call.StepPath!.Contains('.'));
    }

    // Two step methods at once under one Reqnroll step: 0.0 made /first and 0.1 made /second.
    [Fact]
    public void The_issues_commands_read_a_sub_steps_calls_on_a_real_run()
    {
        var file = DataFile("Engine.Reqnroll.xUnit3");
        string scenario;
        using (var index = ReportScanner.Scan(file))
            scenario = index.Scenarios.First(s => s.Name.StartsWith("T19", StringComparison.Ordinal)).Address;

        var whole = Query("interactions", file, scenario, "--step", "0", "--group-by", "step,method,path");
        Assert.Contains("/first", whole);
        Assert.Contains("/second", whole);

        var sub = Query("interactions", file, scenario, "--step", "0.1", "--group-by", "step,method,path");
        Assert.Contains("/second", sub);
        Assert.DoesNotContain("/first", sub);

        var flow = Query("flow", file, $"{scenario}/0.1");
        Assert.Contains("/second", flow);
        Assert.DoesNotContain("/first", flow);
    }

    private static string DataFile(string lane) => ReportRows.DataFile(EngineLane.Run(lane).ReportsDirectory!)!;

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.True(QueryCommand.Run(args, output, error) == 0, error.ToString());
        return output.ToString();
    }
}
