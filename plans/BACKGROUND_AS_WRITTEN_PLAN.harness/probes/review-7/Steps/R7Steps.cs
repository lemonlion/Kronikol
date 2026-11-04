using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Reports;
using Kronikol.Tracking;
using Reqnroll;

namespace R7.Suite;

/// <summary>
/// "r7 a!(b(c!),d)": a step named a whose definition runs b and d, b runs c. After a name, its flags:
///   ! a suite's AfterStep hook throws on this step      ^ a suite's BeforeStep hook, ahead of the adapter's, throws on it
///   c the definition catches what a child threw         x the definition throws after its children
/// </summary>
[Binding]
public class R7Steps(ScenarioContext scenario, ITestRunner runner)
{
    private static readonly ConcurrentDictionary<string, IList<ReqNRollStepInfo>> Lists = new();
    private static readonly object Gate = new();
    private static string OutPath => Environment.GetEnvironmentVariable("KRK_R7_OUT") ?? Path.Combine(Path.GetTempPath(), "krk-r7.txt");

    private static void Log(string line) { lock (Gate) File.AppendAllText(OutPath, line + "\n", new UTF8Encoding(false)); }

    private static (string Name, string Flags, List<string> Children) Parse(string node)
    {
        var open = node.IndexOf('(');
        var head = open < 0 ? node : node[..open];
        var flags = new string(head.Where(c => "!^cxz".Contains(c) && head.IndexOf(c) > 0).ToArray());
        var name = new string(head.Where(c => !flags.Contains(c)).ToArray());
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

    private string Text => scenario.StepContext.StepInfo.Text;
    private (string Name, string Flags, List<string> Children) Here => Text.StartsWith("r7 ") ? Parse(Text[3..]) : ("", "", []);

    // ---- the hooks ----
    [BeforeScenario(Order = 1000)]
    public void KeepTheList()
    {
        if (scenario.TryGetValue(ReqNRollConstants.StepsCollectionKey, out List<ReqNRollStepInfo>? list))
            Lists[scenario.ScenarioInfo.Title + RowOf()] = list!;
    }

    private string RowOf()
    {
        var a = scenario.ScenarioInfo.Arguments;
        return a is { Count: > 0 } ? " #" + string.Join(",", a.Values.Cast<object?>()) : "";
    }

    [BeforeStep(Order = int.MinValue)]
    public void BeforeAhead()
    {
        if (Here.Flags.Contains('^'))
            throw new InvalidOperationException("a BeforeStep hook ahead of the adapter's failed on " + Here.Name);
    }

    private void ReadWatch(string when)
    {
        var read = scenario.TryGetValue(ReqNRollConstants.StepStopwatchKey, out Stopwatch? w) ? (w!.IsRunning ? "running" : "stopped") + " " + (w.ElapsedMilliseconds / 100 * 100) + " ms" : "none";
        Log($"WATCH {scenario.ScenarioInfo.Title}{RowOf()} | {Text} | {when}: {read}");
    }

    [AfterStep(Order = 100)]
    public void AfterHook()
    {
        ReadWatch("suite AfterStep ahead");
        if (Here.Flags.Contains('!'))
            throw new InvalidOperationException("an AfterStep hook failed on " + Here.Name);
    }

    [AfterStep(Order = int.MaxValue)]
    public void AfterLast() => ReadWatch("suite AfterStep last");

    [AfterScenario(Order = 0)]
    public void CountSeen() => Log($"SEEN {scenario.ScenarioInfo.Title}{RowOf()}: {(scenario.TryGetValue(ReqNRollConstants.StepsCollectionKey, out List<ReqNRollStepInfo>? list) ? list!.Count : -1)}");

    [AfterScenario("r7-slowend", Order = 0)]
    public async Task SlowEnd()
    {
        await Task.Delay(1200);
        await CleanUpWoven();
    }

    [Step(Description = "a woven step in the AfterScenario hook")]
    public Task CleanUpWoven() => Task.CompletedTask;

    [AfterTestRun(Order = 0)]
    public static void Dump()
    {
        foreach (var (title, list) in Lists.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Log($"LIST {title}: {list.Count}");
            foreach (var step in list)
            {
                var type = step.GetType();
                var nested = type.GetProperty("Nested", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(step) as System.Collections.IEnumerable;
                var ranBy = type.GetProperty("RanByAStep", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(step);
                var names = nested?.Cast<ReqNRollStepInfo>().Select(n => n.Text.Length > 3 ? n.Text[3..] : n.Text);
                var ms = step.Duration is { } d ? ((long)d.TotalMilliseconds / 100 * 100).ToString() + "ms" : "no time";
                Log($"   {step.Keyword} {step.Text} [{step.Status}] {ms} ranByStep={ranBy} nested=[{(names is null ? "null" : string.Join("; ", names))}]");
            }
        }
    }

    // ---- the steps ----
    [StepDefinition(@"^r7 (.*)$")]
    public async Task Node(string spec)
    {
        var (name, flags, children) = Parse(spec);
        await Task.Delay(name.StartsWith("slow") ? 300 : 0);
        foreach (var child in children)
        {
            if (flags.Contains('c'))
            {
                try { await runner.GivenAsync("r7 " + child); }
                catch (InvalidOperationException e) { Log($"CAUGHT {name}: {e.Message}"); }
            }
            else
            {
                await runner.GivenAsync("r7 " + child);
            }
        }
        if (flags.Contains('z'))
            await Task.Delay(500);
        if (flags.Contains('x'))
            throw new InvalidOperationException("the definition of " + name + " threw");
    }

    // a woven step around a nested step the AfterStep hook fails
    [StepDefinition("r7w woven lets it through")]
    public Task WovenLets() => WovenThrough();

    [Step(Description = "woven through")]
    public Task WovenThrough() => runner.GivenAsync("r7 n!");

    [StepDefinition("r7w woven catches it")]
    public Task WovenCatchesStep() => WovenCatches();

    [Step(Description = "woven catching")]
    public async Task WovenCatches()
    {
        try { await runner.GivenAsync("r7 n!"); }
        catch (InvalidOperationException) { }
    }

    [StepDefinition("r7w woven catches it, then a woven step")]
    public Task WovenCatchesThenStep() => WovenCatchesThen();

    [Step(Description = "woven catching, then more")]
    public async Task WovenCatchesThen()
    {
        try { await runner.GivenAsync("r7 n!"); }
        catch (InvalidOperationException) { }
        await WovenAfter();
    }

    [StepDefinition("r7w woven catches it, then throws")]
    public Task WovenCatchesThrowsStep() => WovenCatchesThrows();

    [Step(Description = "woven catching, then throwing")]
    public async Task WovenCatchesThrows()
    {
        try { await runner.GivenAsync("r7 n!"); }
        catch (InvalidOperationException) { }
        throw new ArgumentException("the woven step's own failure");
    }

    [Step(Description = "woven after")]
    public Task WovenAfter() => Task.CompletedTask;

    [StepDefinition("r7w the definition catches it, then a woven step")]
    public async Task DefinitionCatchesThen()
    {
        try { await runner.GivenAsync("r7 n!"); }
        catch (InvalidOperationException) { }
        await WovenAfter();
    }
}

[Binding]
public static class R7Report
{
    [AfterTestRun(Order = 100)]
    public static void WriteReports() =>
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            SpecificationsTitle = "R7",
            SeparateBackgroundSteps = true,
            TestRunReportToggleDefaults = Environment.GetEnvironmentVariable("KRK_DIAGCLOSED") == "1" ? new ReportToggleDefaults { DiagramsSectionOpen = false } : new ReportToggleDefaults(),
        });
}
