using System.Text;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Reports;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.ErrorHandling;

namespace R8.Suite;

/// <summary>
/// "r8 a~!(b(c~!),d)": a step named a whose definition runs b and d, b runs c. After a name and a tilde, its flags:
///   ! a suite's AfterStep hook throws on this step     W that hook first runs a woven step
///   p that hook throws PendingScenarioException         ^ a suite's BeforeStep hook ahead of the adapter's throws
///   k the definition catches what a child threw         x the definition throws after its children
///   w the definition runs a woven step first            o a woven step wraps the children
/// </summary>
[Binding]
public class R8Steps(ScenarioContext scenario, ITestRunner runner, IReqnrollOutputHelper output)
{
    private static readonly object Gate = new();
    private static string OutPath => Environment.GetEnvironmentVariable("KRK_R8_OUT") ?? Path.Combine(Path.GetTempPath(), "krk-r8.txt");
    internal static void Log(string line) { lock (Gate) File.AppendAllText(OutPath, line + "\n", new UTF8Encoding(false)); }

    private static (string Name, string Flags, List<string> Children) Parse(string node)
    {
        var open = node.IndexOf('(');
        var head = open < 0 ? node : node[..open];
        var tilde = head.IndexOf('~');
        var flags = tilde < 0 ? "" : head[(tilde + 1)..];
        var name = tilde < 0 ? head : head[..tilde];
        var children = new List<string>();
        if (open >= 0)
        {
            var body = node[(open + 1)..node.LastIndexOf(')')];
            var depth = 0; var start = 0;
            for (var i = 0; i < body.Length; i++)
            {
                if (body[i] == '(') depth++;
                else if (body[i] == ')') depth--;
                else if (body[i] == ',' && depth == 0) { children.Add(body[start..i]); start = i + 1; }
            }
            children.Add(body[start..]);
        }
        return (name, flags, children);
    }

    private void Attach(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "krk-r8");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, name + ".txt");
        File.WriteAllText(file, name);
        output.AddAttachment(file);
    }

    private string Text => scenario.StepContext?.StepInfo.Text ?? "";
    private (string Name, string Flags, List<string> Children) Here => Text.StartsWith("r8 ") ? Parse(Text[3..]) : ("", "", []);
    private string Title => scenario.ScenarioInfo.Title;

    private List<ReqNRollStepInfo>? List => scenario.TryGetValue(ReqNRollConstants.StepsCollectionKey, out List<ReqNRollStepInfo>? list) ? list : null;
    private string ListText => List is null ? "no list" : "[" + string.Join("; ", List.Select(s => s.Text[(s.Text.StartsWith("r8 ") ? 3 : 0)..] + ":" + s.Status)) + "]";
    private string HasActive => "active=" + StepCollector.HasActiveStep(scenario.TryGetValue(ReqNRollConstants.ScenarioRuntimeIdKey, out string? id) ? id : null);

    private static int _dumped;
    [BeforeScenario(Order = 1)]
    public void DumpHooksOnce()
    {
        if (Interlocked.Exchange(ref _dumped, 1) == 1) return;
        var registry = scenario.ScenarioContainer.Resolve<Reqnroll.Bindings.IBindingRegistry>();
        foreach (var type in new[] { Reqnroll.Bindings.HookType.AfterScenario, Reqnroll.Bindings.HookType.AfterStep })
            foreach (var hook in registry.GetHooks(type).OrderBy(h => h.HookOrder))
                Log($"HOOK {type} order={hook.HookOrder} {hook.Method.Type.FullName}.{hook.Method.Name}");
    }

    // ---- step hooks ----
    [BeforeStep(Order = int.MinValue)]
    public void BeforeAhead()
    {
        if (Here.Flags.Contains('^'))
            throw new InvalidOperationException("suite BeforeStep ahead threw on " + Here.Name);
    }

    [AfterStep(Order = 100)]
    public void AfterHook()
    {
        if (Here.Flags.Contains('W')) Woven_in_after_step(Here.Name);
        if (Here.Flags.Contains('a')) Attach("in-after-step");
        if (Here.Flags.Contains('i')) scenario.ScenarioContainer.Resolve<Reqnroll.UnitTestProvider.IUnitTestRuntimeProvider>().TestIgnore("hook ignores on " + Here.Name);
        if (Here.Flags.Contains('!')) throw new InvalidOperationException("suite AfterStep threw on " + Here.Name);
        if (Here.Flags.Contains('p')) throw new PendingScenarioException("suite AfterStep pending on " + Here.Name);
    }

    [Step(Description = "woven in after step")] public void Woven_in_after_step(string on) { }

    // ---- AfterScenario hooks at various orders; each only for a scenario with its tag ----
    private void Report(string where) => Log($"{where} | {Title} | {ListText} | {HasActive} | status={scenario.ScenarioExecutionStatus}");

    [AfterScenario(Order = int.MinValue)] public void AtMin()
    {
        if (scenario.ScenarioInfo.Tags.Contains("min-read")) Report("AS min");
        if (scenario.ScenarioInfo.Tags.Contains("min-woven")) Woven_in_after_scenario("at min");
        if (scenario.ScenarioInfo.Tags.Contains("min-attach")) Attach("at-min");
        if (scenario.ScenarioInfo.Tags.Contains("min-throw")) throw new InvalidOperationException("suite AfterScenario at int.MinValue threw");
    }

    [AfterScenario(Order = int.MinValue + 1)] public void AtMinPlus()
    {
        if (scenario.ScenarioInfo.Tags.Contains("minp-read")) Report("AS min+1");
        if (scenario.ScenarioInfo.Tags.Contains("minp-woven")) Woven_in_after_scenario("at min+1");
    }

    [AfterScenario(Order = 0)] public async Task AtZero()
    {
        if (scenario.ScenarioInfo.Tags.Contains("z-read")) Report("AS 0");
        if (scenario.ScenarioInfo.Tags.Contains("z-woven")) Woven_in_after_scenario("at 0");
        if (scenario.ScenarioInfo.Tags.Contains("z-attach")) Attach("at-0");
        if (scenario.ScenarioInfo.Tags.Contains("z-woven-fail")) { try { Woven_that_fails("at 0"); } catch (InvalidOperationException) { } }
        if (scenario.ScenarioInfo.Tags.Contains("z-step")) await runner.GivenAsync("r8 cleanup");
        if (scenario.ScenarioInfo.Tags.Contains("z-step-throw")) { try { await runner.GivenAsync("r8 cleanup~!"); } catch (InvalidOperationException e) { Log("CAUGHT at 0: " + e.Message); } }
        if (scenario.ScenarioInfo.Tags.Contains("z-throw")) throw new InvalidOperationException("suite AfterScenario at 0 threw");
        if (scenario.ScenarioInfo.Tags.Contains("z-tamper")) List?.Clear();
    }

    [AfterScenario(Order = int.MaxValue)] public void AtMax()
    {
        if (scenario.ScenarioInfo.Tags.Contains("max-read")) Report("AS max");
        if (scenario.ScenarioInfo.Tags.Contains("max-woven")) Woven_in_after_scenario("at max");
    }

    [BeforeScenario("bs-step", Order = 5)] public async Task BeforeRunsStep()
    {
        await runner.GivenAsync("r8 pre~!");
    }

    [BeforeScenario(Order = 0)] public void BeforeWoven()
    {
        if (scenario.ScenarioInfo.Tags.Contains("bs-woven")) Woven_in_before_scenario("at 0");
    }

    [Step(Description = "woven in before scenario")] public void Woven_in_before_scenario(string at) { }
    [Step(Description = "woven in after scenario")] public void Woven_in_after_scenario(string at) { }
    [Step(Description = "woven that fails")] public void Woven_that_fails(string at) => throw new InvalidOperationException("woven failed " + at);
    [Step(Description = "woven in definition")] public void Woven_in_definition(string who) { }

    // ---- the steps ----
    [StepDefinition(@"^r8 (.*)$")]
    public async Task Node(string spec)
    {
        var (name, flags, children) = Parse(spec);
        if (flags.Contains('w')) Woven_in_definition(name);
        if (flags.Contains('o')) await Wrap(children, flags.Contains('k'));
        else await RunChildren(children, flags.Contains('k'));
        if (flags.Contains('x'))
            throw new InvalidOperationException("the definition of " + name + " threw");
    }

    private async Task RunChildren(List<string> children, bool catching)
    {
        foreach (var child in children)
        {
            if (catching)
            {
                try { await runner.GivenAsync("r8 " + child); }
                catch (InvalidOperationException e) { Log($"CAUGHT in {Title}: {e.Message}"); }
            }
            else
            {
                await runner.GivenAsync("r8 " + child);
            }
        }
    }

    [Step(Description = "woven wrapper")]
    public Task Wrap(List<string> children, bool catching) => RunChildren(children, catching);
}

[Binding]
public static class R8Report
{
    [AfterTestRun(Order = 100)]
    public static void WriteReports() =>
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            SpecificationsTitle = "R8",
            SeparateBackgroundSteps = true,
        });
}
