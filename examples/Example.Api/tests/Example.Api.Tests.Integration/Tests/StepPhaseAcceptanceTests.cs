using Example.Api.Tests.Integration.Helpers;
using Kronikol.Query;

namespace Example.Api.Tests.Integration.Tests;

/// <summary>
/// #224's acceptance on a real suite (plans/STEP_PHASE_PLAN.md F2): a LightBDD keyword is upper case and each step runs in an
/// async frame of its own, so a call the test made read no phase unless it was an HTTP call before the first When's. The query
/// reads a call's phase, which <see cref="ReportParser"/> does not.
/// </summary>
[Collection("SequentialTests")]
public class StepPhaseAcceptanceTests
{
    [Fact]
    public async Task Every_test_side_call_in_a_LightBDD_scenario_has_a_phase()
    {
        var result = await TestProjectRunner.RunAsync(TestProjects.LightBddXUnit2, new Dictionary<string, string>
        {
            ["KRONIKOL_SEPARATE_SETUP"] = "true"
        });
        Assert.True(result.Success, $"{TestProjects.LightBddXUnit2} failed:\n{result.StandardError}\n{result.StandardOutput}");

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = QueryCommand.Run(["interactions", result.ReportsFolderPath, "--group-by", "phase,caller"], output, error);
        Assert.True(exit == 0, $"query exited {exit}:\n{error}\n{output}");

        // One line per phase and caller, the phase first and "-" for none. Only the suite's own calls are the Caller's: a call the
        // service makes runs in the server's flow, and takes its phase from its parent later.
        var rows = output.ToString().ReplaceLineEndings("\n").Split('\n')
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(cells => cells.Length > 1 && cells[1] == "Caller")
            .Select(cells => cells[0])
            .ToArray();

        Assert.Contains("Setup", rows);
        Assert.Contains("Action", rows);
        Assert.DoesNotContain("-", rows);
    }
}
