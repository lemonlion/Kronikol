p = 'C:/Code/k225-fresh3/probe/Program.cs'
s = open(p, encoding='utf-8').read()
marker = 'if (part is "crash" or "all")'
add = r'''if (part is "collector")
{
    // The real StepCollector, called as BDDfyStepTrackingExecutor calls it: keyword by execution order, text the title with that keyword stripped.
    var collector = assembly.GetType("Kronikol.Tracking.StepCollector")!;
    var start = collector.GetMethods(flags).Where(m => m.Name == "StartStep").OrderByDescending(m => m.GetParameters().Length).First();
    var complete = collector.GetMethods(flags).Where(m => m.Name == "CompleteStep").OrderBy(m => m.GetParameters().Length).First();
    void Step(string id, string? kw, string text)
    {
        var a = new object?[start.GetParameters().Length];
        a[0] = id; a[1] = kw; a[2] = text;
        start.Invoke(null, a);
        var c = new object?[complete.GetParameters().Length];
        c[0] = id; c[1] = true;
        complete.Invoke(null, c);
    }
    var longText = string.Join(" ", Enumerable.Repeat("word", 40));
    var rows = new (string? Keyword, string Text)[] { ("Then", @"x at C:\temp"), ("And", @"y at C:\temp"), ("But", longText), ("And", "short one") };
    Step("b", "Then", @"x at C:\temp");
    Step("b", "Then", @"And y at C:\temp");
    Step("b", "Then", "But " + longText);
    Step("b", "Then", "And short one");

    var inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var loggerType = assembly.GetTypes().First(t => t.Name == "RequestResponseLogger");
    var member = (object?)loggerType.GetProperty("RequestAndResponseLogs", flags)?.GetValue(null) ?? loggerType.GetField("RequestAndResponseLogs", flags)?.GetValue(null);
    var bars = new List<string>();
    foreach (var log in (System.Collections.IEnumerable)member!)
    {
        var plantUml = log.GetType().GetProperty("PlantUml", inst)?.GetValue(log) as string;
        var id = log.GetType().GetProperty("TestId", inst)?.GetValue(log) as string;
        if (plantUml is not null && id == "b") bars.Add(plantUml);
    }
    Console.WriteLine($"{bars.Count} bars from the real collector");
    for (var i = 0; i < Math.Min(bars.Count, rows.Length); i++)
    {
        var m = bars[i].Replace('\n', ' ').Trim();
        Console.WriteLine($"row ({rows[i].Keyword}, {Show(rows[i].Text)}): bar {Show(m[..Math.Min(m.Length, 130)])}\n   published {Published(m, rows[i].Text)}  DrawsStep {drawsStep(m, rows[i].Keyword, rows[i].Text)}");
    }
}

'''
assert marker in s
s = s.replace(marker, add + marker, 1)
open(p, 'w', encoding='utf-8').write(s)

m = 'C:/Code/k225-fresh3/make_cases3.py'
t = open(m, encoding='utf-8').read()
t += r'''case("B1-bddfy-and-under-and-escape", [("s", "Then", r"x at C:\temp"), ("s", "And", r"y at C:\temp")],
     [("bar", f"Then x at C:{e}temp"), ("call", "x"), ("bar", f"And And y at C:{e}temp"), ("call", "y")])
case("B2-bddfy-and-under-and-short-control", [("s", "Then", "x"), ("s", "And", "y")],
     [("bar", "Then x"), ("call", "x"), ("bar", "And And y"), ("call", "y")])
'''
open(m, 'w', encoding='utf-8').write(t)
