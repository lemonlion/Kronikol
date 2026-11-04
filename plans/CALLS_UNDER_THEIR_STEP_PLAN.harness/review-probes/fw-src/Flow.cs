using Kronikol.Reports;
using Kronikol.Tracking;

[assembly: TrackSteps]

namespace Fw;

public class FlowDriver
{
    public static string Id => Track.TestIdResolver?.Invoke();

    public static void Call(string path)
    {
        var id = Id;
        if (id is not null)
            RequestResponseLogger.LogPair(id, id, "GET", new Uri("http://svc/" + path), "svc", "caller");
    }

    [GivenStep(Description = "seed sync")]
    public void SeedSync() => Call("seed-sync");

    [GivenStep(Description = "seed async")]
    public async Task SeedAsync()
    {
        Call("seed-async/1");
        await Task.Yield();
        Call("seed-async/2");
    }

    [WhenStep(Description = "body")]
    public async Task Body()
    {
        Call("body/before");
        await Sub();
        Call("body/after");
        Track.That(() => { });
        await Task.Yield();
    }

    [Step(Description = "sub")]
    public async Task Sub()
    {
        await Task.Yield();
        Call("sub");
    }

    [ThenStep(Description = "teardown step")]
    public async Task TearDownStep()
    {
        await Task.Yield();
        Call("teardown");
    }
}

/// <summary>Three shapes run through each framework's own hooks.</summary>
public sealed class Flow(string variant)
{
    private readonly FlowDriver _d = new();
    private string _id;

    public void SetupSync()
    {
        _id = FlowDriver.Id;
        _d.SeedSync();
        if (variant == "B")
            StepCollector.StartStep(FlowDriver.Id, "Given", "wrapper opened in a sync setup", null, null);
    }

    public async Task SetupAsync()
    {
        _id ??= FlowDriver.Id;
        if (variant == "A")
            await _d.SeedAsync();
        if (variant == "C")
        {
            StepCollector.StartStep(FlowDriver.Id, "Given", "wrapper opened in an async setup", null, null);
            await Task.Yield();
        }
    }

    public static int Attempts;

    public async Task Test()
    {
        _id ??= FlowDriver.Id;
        if (variant == "R")
        {
            await _d.Body();
            if (Interlocked.Increment(ref Attempts) == 1)
                throw new InvalidOperationException("first attempt fails");
            return;
        }
        if (variant == "A")
        {
            await _d.Body();
        }
        else
        {
            FlowDriver.Call("body");
            Track.That(() => { });
            await _d.Sub();
        }
    }

    public async Task TeardownAsync()
    {
        if (variant == "A")
            await _d.TearDownStep();
    }

    public void TeardownSync()
    {
        if (variant is "B" or "C")
            StepCollector.CompleteStep(FlowDriver.Id, false, "wrapper failed");
    }

    public void Print(string framework)
    {
        var id = _id;
        var steps = StepCollector.GetSteps(id);
        var scenario = new Scenario { Id = id, DisplayName = variant, Steps = steps.Length > 0 ? steps : null };
        var feature = new Feature { DisplayName = "F", Scenarios = [scenario] };
        var logs = RequestResponseLogger.RequestAndResponseLogs;
        var paths = ReportGenerator.AttributeInteractionsToStepPaths(logs, [feature]);
        var text = new System.Text.StringBuilder();
        text.AppendLine($"=== {framework} variant {variant}");
        Tree(text, steps, "", "  ");
        if (paths.TryGetValue(id, out var list))
        {
            var records = logs.Where(l => l.TestId == id && !l.IsDiagramMarker).ToArray();
            for (var i = 0; i < records.Length; i++)
                if (records[i].Type == RequestResponseType.Request)
                    text.AppendLine($"  call {records[i].Uri.AbsolutePath,-20} -> {list[i] ?? "null"}");
        }
        text.AppendLine($"  scenario attachments: {StepCollector.GetScenarioAttachments(id)?.Length ?? 0}; still active: {StepCollector.HasActiveStep(id)}");
        lock (typeof(Flow))
            File.AppendAllText(Environment.GetEnvironmentVariable("FW_OUT") ?? "fw-out.txt", text.ToString());
    }

    private static void Tree(System.Text.StringBuilder text, ScenarioStep[] steps, string prefix, string indent)
    {
        if (steps is null) return;
        for (var i = 0; i < steps.Length; i++)
        {
            var s = steps[i];
            var path = prefix + i;
            text.AppendLine($"{indent}{path,-8} [{s.Status}] {(s.Keyword is null ? "" : s.Keyword + " ")}{s.Text}{(s.FailureMessage is null ? "" : "  msg=" + s.FailureMessage)}");
            Tree(text, s.SubSteps, path + ".", indent + "  ");
        }
    }
}
