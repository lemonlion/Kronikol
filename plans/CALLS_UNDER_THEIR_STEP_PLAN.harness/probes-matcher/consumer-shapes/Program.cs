using System.Net;
using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;

var shape = args[0];
var dir = Path.GetFullPath(args[1]);
Directory.CreateDirectory(dir);
var options = new ReportConfigurationOptions
{
    ReportsFolderPath = dir,
    InternalFlowTracking = false,
    GenerateComponentDiagram = false,
    WriteRunSummaryToConsole = false,
};

void Call(string testId, string path) =>
    RequestResponseLogger.LogPair("t", testId, HttpMethod.Post, new Uri("http://shop.test/" + path), "Shop", "Tests", "{}", "{}", HttpStatusCode.OK);

void Step(string id, string? keyword, string text, string path)
{
    StepCollector.StartStep(id, keyword, text, null, null);
    Call(id, path);
    StepCollector.CompleteStep(id, true);
}

ScenarioStep Rebuilt(ScenarioStep c) => new() { Keyword = c.Keyword, Text = c.Text, Status = c.Status, SubSteps = c.SubSteps, Duration = c.Duration };

Feature One(Scenario s) => new() { DisplayName = shape, Scenarios = [s] };

Feature[] features;
var start = DateTime.UtcNow;
switch (shape)
{
    case "plain-control":
    {
        const string id = "c0";
        Step(id, "Given", "a thing", "given");
        Step(id, "When", "another thing", "when");
        features = [One(new Scenario { Id = id, DisplayName = "control", Result = ExecutionResult.Passed, Steps = StepCollector.GetSteps(id) })];
        break;
    }
    case "mix-new-row":
    {
        // A consumer's adapter: keeps a collected row as it is, and rebuilds the row it has more to say about.
        const string id = "m0";
        Step(id, "Given", "a thing", "given");
        Step(id, "When", "another thing", "when");
        Step(id, "Then", "the last thing", "then");
        var rows = StepCollector.GetSteps(id).Select(c => c.Keyword == "When" ? Rebuilt(c) with { Comments = ["annotated by the adapter"] } : c).ToArray();
        features = [One(new Scenario { Id = id, DisplayName = "one row rebuilt", Result = ExecutionResult.Passed, Steps = rows })];
        break;
    }
    case "mix-background":
    {
        // A consumer's adapter: the opening rows rebuilt into BackgroundSteps, the others as collected.
        const string id = "m1";
        Step(id, "Given", "a shop", "shop");
        Step(id, "When", "a basket", "basket");
        Step(id, "Then", "it is empty", "check");
        var collected = StepCollector.GetSteps(id);
        features = [One(new Scenario { Id = id, DisplayName = "background rebuilt", Result = ExecutionResult.Passed, BackgroundSteps = [Rebuilt(collected[0])], Steps = collected[1..] })];
        break;
    }
    case "hand-first-line":
    {
        // Hand-built rows, read against the bars. An extra bar first, and two rows sharing a first line.
        const string id = "h0";
        Step(id, null, "Seed the shop", "seed");
        Step(id, "When", "the order is placed\nfor cash", "cash");
        Step(id, "And", "the order is placed\nfor card", "card");
        Step(id, "Then", "it is confirmed", "confirm");
        features = [One(new Scenario { Id = id, DisplayName = "shared first line", Result = ExecutionResult.Passed, Steps =
        [
            new ScenarioStep { Keyword = "When", Text = "the order is placed\nfor cash", Status = ExecutionResult.Passed },
            new ScenarioStep { Keyword = "And", Text = "the order is placed\nfor card", Status = ExecutionResult.Passed },
            new ScenarioStep { Keyword = "Then", Text = "it is confirmed", Status = ExecutionResult.Passed },
        ] })];
        break;
    }
    case "hand-400":
    {
        const string id = "h1";
        var opening = "the customer " + new string('x', 390);
        Step(id, null, "Seed the shop", "seed");
        Step(id, "When", opening + " pays cash", "cash");
        Step(id, "And", opening + " pays card", "card");
        Step(id, "Then", "it is confirmed", "confirm");
        features = [One(new Scenario { Id = id, DisplayName = "shared 400", Result = ExecutionResult.Passed, Steps =
        [
            new ScenarioStep { Keyword = "When", Text = opening + " pays cash", Status = ExecutionResult.Passed },
            new ScenarioStep { Keyword = "And", Text = opening + " pays card", Status = ExecutionResult.Passed },
            new ScenarioStep { Keyword = "Then", Text = "it is confirmed", Status = ExecutionResult.Passed },
        ] })];
        break;
    }
    case "hand-fold":
    {
        const string id = "h2";
        Step(id, null, "Seed the shop", "seed");
        Step(id, "When", "a new order", "n");
        Step(id, "And", "anew order", "m");
        Step(id, "Then", "it is confirmed", "confirm");
        features = [One(new Scenario { Id = id, DisplayName = "fold", Result = ExecutionResult.Passed, Steps =
        [
            new ScenarioStep { Keyword = "When", Text = "a new order", Status = ExecutionResult.Passed },
            new ScenarioStep { Keyword = "And", Text = "anew order", Status = ExecutionResult.Passed },
            new ScenarioStep { Keyword = "Then", Text = "it is confirmed", Status = ExecutionResult.Passed },
        ] })];
        break;
    }
    case "manual-bar":
    {
        // A step bar a consumer draws through the public override, rows from the collector
        const string id = "b0";
        Step(id, "Given", "a thing", "given");
        DefaultTrackingDiagramOverride.InsertPlantUml(id, "hnote across <<stepDelimiter>> #black:<color:white>Cleanup", DiagramMarkerKind.Step);
        Call(id, "cleanup");
        features = [One(new Scenario { Id = id, DisplayName = "manual bar", Result = ExecutionResult.Passed, Steps = StepCollector.GetSteps(id) })];
        break;
    }
    case "with-copy":
    {
        // A consumer's adapter renames a row through with: the id comes along
        const string id = "w0";
        Step(id, "Given", "a thing", "given");
        Step(id, "When", "another thing", "when");
        var rows = StepCollector.GetSteps(id).Select(c => c with { Text = c.Text + " (renamed)" }).ToArray();
        features = [One(new Scenario { Id = id, DisplayName = "renamed", Result = ExecutionResult.Passed, Steps = rows })];
        break;
    }
    case "prepend-hand-row":
    {
        // A consumer's adapter adds a row of its own in front of the collected ones; it drew no bar
        const string id = "w1";
        Step(id, "Given", "a thing", "given");
        Step(id, "When", "another thing", "when");
        features = [One(new Scenario { Id = id, DisplayName = "row in front", Result = ExecutionResult.Passed,
            Steps = [new ScenarioStep { Keyword = "Given", Text = "the adapter's own row", Status = ExecutionResult.Passed }, .. StepCollector.GetSteps(id)] })];
        break;
    }
    case "hand-manual-raw":
    {
        // A consumer's own lane: its own rows, its own bars through the public override, as it always wrote them
        const string id = "w2";
        void Bar(string label) => DefaultTrackingDiagramOverride.InsertPlantUml(id, "hnote across <<stepDelimiter>> #black:<color:white>" + label, DiagramMarkerKind.Step);
        Bar(@"When the file C:\temp\new is read"); Call(id, "read");
        Bar("And a value <$name> is sent"); Call(id, "send");
        Bar("Then it is confirmed"); Call(id, "confirm");
        features = [One(new Scenario { Id = id, DisplayName = "own rows own bars", Result = ExecutionResult.Passed, Steps =
        [
            new ScenarioStep { Keyword = "When", Text = @"the file C:\temp\new is read", Status = ExecutionResult.Passed },
            new ScenarioStep { Keyword = "And", Text = "a value <$name> is sent", Status = ExecutionResult.Passed },
            new ScenarioStep { Keyword = "Then", Text = "it is confirmed", Status = ExecutionResult.Passed },
        ] })];
        break;
    }
    case "issue-128":
    {
        // #128's own repro: "Given " + the first text passes 110 characters, "And " + the second stays under it.
        const string id = "i128";
        Step(id, "Given", "a basket that held two apples and a pear last week but nothing at all this week, while the shop's prices rose", "a");
        Step(id, "And", "a basket that held two apples and a pear last week but nothing this week, while the shop's prices rose", "b");
        Step(id, "When", "the shopper opens the basket page", "c");
        features = [One(new Scenario { Id = id, DisplayName = "basket", Result = ExecutionResult.Passed, Steps =
        [
            new ScenarioStep { Keyword = "Given", Text = "a basket that held two apples and a pear last week but nothing at all this week, while the shop's prices rose" },
            new ScenarioStep { Keyword = "And", Text = "a basket that held two apples and a pear last week but nothing this week, while the shop's prices rose" },
            new ScenarioStep { Keyword = "When", Text = "the shopper opens the basket page" },
        ] })];
        break;
    }
    case "hand-tail-plain":
    {
        // A row's text the tail of the next bar's label, with an extra bar first: published's own loose check takes it.
        const string id = "t0";
        Step(id, null, "Seed the shop", "seed");
        Step(id, "When", "we read the file", "read");
        Step(id, "And", "the file", "tail");
        features = [One(new Scenario { Id = id, DisplayName = "tail", Result = ExecutionResult.Passed, Steps =
        [
            new ScenarioStep { Keyword = "When", Text = "we read the file" },
            new ScenarioStep { Keyword = "And", Text = "the file" },
        ] })];
        break;
    }
    case "hand-tail-escaped":
    {
        // The same on text the bar escapes: published refuses it, and the second form must too.
        const string id = "t1";
        var path = "C:" + (char)92 + "temp";
        Step(id, null, "Seed the shop", "seed");
        Step(id, "When", "we read " + path, "read");
        Step(id, "And", path, "tail");
        features = [One(new Scenario { Id = id, DisplayName = "tail", Result = ExecutionResult.Passed, Steps =
        [
            new ScenarioStep { Keyword = "When", Text = "we read " + path },
            new ScenarioStep { Keyword = "And", Text = path },
        ] })];
        break;
    }
    case "hand-long-no-keyword":
    {
        // Rows with no keyword under bars that have one: the first word is what the bar adds.
        const string id = "t2";
        Step(id, "Given", "a basket that held two apples and a pear last week but nothing at all this week, while the shop's prices rose", "a");
        Step(id, "When", "the shopper opens the basket page", "b");
        features = [One(new Scenario { Id = id, DisplayName = "no keyword on the row", Result = ExecutionResult.Passed, Steps =
        [
            new ScenarioStep { Text = "a basket that held two apples and a pear last week but nothing at all this week, while the shop's prices rose" },
            new ScenarioStep { Text = "the shopper opens the basket page" },
        ] })];
        break;
    }
    case "null-test-id":
    {
        const string id = "good-1";
        Step(id, "Given", "a thing", "good");
        RequestResponseLogger.LogPair("t", null!, HttpMethod.Post, new Uri("http://shop.test/no-id"), "Shop", "Tests", "{}", "{}", HttpStatusCode.OK);
        features = [One(new Scenario { Id = id, DisplayName = "good", Result = ExecutionResult.Passed, Steps = StepCollector.GetSteps(id) })];
        break;
    }
    case "null-scenario-id":
    {
        const string id = "good-2";
        Step(id, "Given", "a thing", "good");
        features = [new Feature { DisplayName = shape, Scenarios =
        [
            new Scenario { Id = id, DisplayName = "good", Result = ExecutionResult.Passed, Steps = StepCollector.GetSteps(id) },
            new Scenario { Id = null!, DisplayName = "no id", Result = ExecutionResult.Passed },
        ] }];
        break;
    }
    default:
        throw new ArgumentException(shape);
}

try
{
    ReportGenerator.CreateStandardReportsWithDiagrams(features, start, DateTime.UtcNow, options);
    Console.WriteLine("generator returned");
}
catch (Exception ex)
{
    Console.WriteLine($"generator threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
}
Console.WriteLine("files: " + string.Join(" ", Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileName) : []));
