// A store really projected from an in-process run: the collector draws the bars, NdjsonInteractionWriter writes the store,
// and a tests file lists the rows the way a consumer's converter would. Each case is one ingest.
using System.Net;
using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Tracking;

var root = Path.GetFullPath(args[0]);
Directory.CreateDirectory(root);

void Call(string testId, string path) =>
    RequestResponseLogger.LogPair("t", testId, HttpMethod.Post, new Uri("http://shop.test/" + path), "Shop", "Tests", "{}", "{}", HttpStatusCode.OK);

var tests = new Dictionary<string, List<Dictionary<string, object?>>>();
var owner = new Dictionary<string, string>();

string Case(string name, string id, string title)
{
    tests[name] = [new() { ["event"] = "start", ["testId"] = id, ["testName"] = title, ["feature"] = "Store", ["timestamp"] = DateTime.UtcNow }];
    owner[id] = name;
    return id;
}

void Row(string id, string? keyword, string text, bool? background = null, bool timed = true, string status = "passed", int? level = null, string? doc = null)
{
    var row = new Dictionary<string, object?> { ["event"] = "step", ["testId"] = id, ["text"] = text, ["status"] = status, ["durationMs"] = 5 };
    if (keyword is not null) row["keyword"] = keyword;
    if (background is not null) row["background"] = background;
    if (timed) row["timestamp"] = DateTime.UtcNow;
    if (level is not null) row["level"] = level;
    if (doc is not null) row["docString"] = doc;
    tests[owner[id]].Add(row);
}

// The step in the run, and its row in the tests file
void Step(string id, string? keyword, string text, string path, bool? background = null, bool timed = true, string? rowText = null, bool list = true)
{
    if (list) Row(id, keyword, rowText ?? text, background, timed);
    StepCollector.StartStep(id, keyword, text, null, null);
    Call(id, path);
    StepCollector.CompleteStep(id, true);
    Thread.Sleep(2);
}

void End(string id) => tests[owner[id]].Add(new() { ["event"] = "end", ["testId"] = id, ["status"] = "passed", ["durationMs"] = 50, ["timestamp"] = DateTime.UtcNow });

// 1. Rows as written, timed
{
    var id = Case("S1-store-plain", "s1", "plain");
    Step(id, "Given", "a shop", "shop");
    Step(id, "When", "a basket", "basket");
    Step(id, "Then", "it is empty", "check");
    End(id);
}
// 2. The tests file marks a Background, the store drew its bars too: the projected form of L
{
    var id = Case("S2-store-background", "s2", "background");
    Step(id, "Given", "a shop", "shop", background: true);
    Step(id, "And", "a customer", "customer", background: true);
    Step(id, "When", "a basket", "basket");
    Step(id, "Then", "it is empty", "check");
    End(id);
}
// 3. Rows untimed: every row marked, every bar the store's
{
    var id = Case("S3-store-untimed", "s3", "untimed");
    Step(id, "Given", "a shop", "shop", timed: false);
    Step(id, "When", "a basket", "basket", timed: false);
    Step(id, "Then", "it is empty", "check", timed: false);
    End(id);
}
// 4. What a bar wraps or escapes: long, a path, a sprite, brackets
{
    var id = Case("S4-store-escapes", "s4", "escapes");
    Step(id, "Given", "the customer " + string.Concat(Enumerable.Repeat("with a very long name indeed ", 8)) + "exists", "long");
    Step(id, "When", @"the file C:\temp\new is read", "read");
    Step(id, "And", "a value <$name> and [link] and **bold** is sent", "send");
    Step(id, "Then", "it is confirmed", "confirm");
    End(id);
}
// 5. Two lines
{
    var id = Case("S5-store-two-lines", "s5", "two lines");
    Step(id, "When", "the order is placed\nfor cash", "cash");
    Step(id, "And", "the order is placed\nfor card", "card");
    Step(id, "Then", "it is confirmed", "confirm");
    End(id);
}
// 6. A step of the run the tests file does not list, ahead of the rows (a hook's step). No row names it, so the rows are one out
{
    var id = Case("S6-store-hook-bar", "s6", "hook bar");
    Step(id, null, "Seed the shop", "seed", list: false);
    Step(id, "Given", "a shop", "shop");
    Step(id, "When", "a basket", "basket");
    End(id);
}
// 7. The same, with a marked Background behind the hook's bar
{
    var id = Case("S7-store-hook-bar-background", "s7", "hook bar background");
    Step(id, null, "Seed the shop", "seed", list: false);
    Step(id, "Given", "a shop", "shop", background: true);
    Step(id, "When", "a basket", "basket");
    Step(id, "Then", "it is empty", "check");
    End(id);
}
// 8. An outline's row: the converter lists the step as written, the run drew it with its value
{
    var id = Case("S8-store-outline-row", "s8", "outline row");
    Step(id, "Given", "a shop named Acme", "shop", background: true, rowText: "a shop named <name>");
    Step(id, "When", "a basket", "basket");
    Step(id, "Then", "it is empty", "check");
    End(id);
}
// 9. The same with untimed rows, and no Background
{
    var id = Case("S9-store-outline-untimed", "s9", "outline untimed");
    Step(id, "Given", "a basket", "basket", timed: false);
    Step(id, "When", "an order with 5", "order", timed: false, rowText: "an order with <amount>");
    Step(id, "Then", "it is confirmed", "confirm", timed: false);
    End(id);
}
// 10. A row with sub-steps, as a nested tests file lists them
{
    var id = Case("S10-store-nested", "s10", "nested");
    Row(id, "Given", "a shop");
    StepCollector.StartStep(id, "Given", "a shop", null, null);
    Row(id, null, "seed the stock", level: 1);
    StepCollector.StartStep(id, null, "seed the stock", null, null);
    Call(id, "seed");
    StepCollector.CompleteStep(id, true);
    Call(id, "shop");
    StepCollector.CompleteStep(id, true);
    Step(id, "Then", "it is empty", "check");
    End(id);
}
// 11. A tests file that names a step the run never drew (a step that did not run)
{
    var id = Case("S11-store-row-not-run", "s11", "row not run");
    Step(id, "Given", "a shop", "shop");
    Row(id, "When", "a step that did not run", timed: false);
    Step(id, "Then", "it is empty", "check");
    End(id);
}
// 12. The same text twice
{
    var id = Case("S12-store-same-text", "s12", "same text");
    Step(id, "Given", "a basket", "one");
    Step(id, "And", "a basket", "two");
    Step(id, "Then", "it is empty", "check");
    End(id);
}

var logs = RequestResponseLogger.RequestAndResponseLogs;
foreach (var (name, rows) in tests)
{
    var dir = Path.Combine(root, name);
    Directory.CreateDirectory(Path.Combine(dir, "captures"));
    var id = (string)rows[0]["testId"]!;
    using (var writer = new NdjsonInteractionWriter(Path.Combine(dir, "captures", "calls.ndjson")))
    {
        foreach (var log in logs.Where(l => l.TestId == id))
            writer.Log(log);
    }
    File.WriteAllLines(Path.Combine(dir, "tests.ndjson"), rows.Select(r => JsonSerializer.Serialize(r)));
}
Console.WriteLine($"{tests.Count} cases, {logs.Length} logs");
