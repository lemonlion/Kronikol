#:package Kronikol@5.19.0
#:property PublishAot=false
// Which verbs take a scenario by sid: at 5.19.0, on the report gaps.cs wrote (two scenarios share one stableId there,
// so this report's s0 is given by the stableId of a report with one: the refusal text tells "not taken" from "ambiguous").
using Kronikol.Query;
var report = args[0];
foreach (var verb in new[] { "steps", "flow", "interactions", "services", "parties", "values", "diagram", "failures", "scenarios" })
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    var exit = QueryCommand.Run([verb, report, "sid:7b41a99eb768004c"], output, error);
    var said = error.ToString().Split('\n').FirstOrDefault(l => l.Trim().Length > 0 && !l.StartsWith('!'))?.Trim() ?? "";
    Console.WriteLine($"{verb,-13} exit {exit}  {said}");
}
