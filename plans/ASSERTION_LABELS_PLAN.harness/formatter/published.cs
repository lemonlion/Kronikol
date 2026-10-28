#:package Kronikol@5.12.0
#:property TargetFramework=net10.0
// The published formatter over a corpus, as probe.cs does over the worktree's: "statement => label". A "\n" in a
// corpus line stands for a line break, as [CallerArgumentExpression] and a sequence point keep them, and a label's
// line breaks print as "\n" so one label stays on one line here.
using Kronikol.Tracking;

Console.OutputEncoding = System.Text.Encoding.UTF8;

foreach (var line in File.ReadAllLines(args[0]))
{
    if (line.Length == 0 || line.StartsWith("#"))
        continue;
    var label = AssertionExpressionFormatter.Format(line.Replace("\\n", "\n"));
    Console.WriteLine($"{line}\n    => {label.Replace("\n", "\\n")}");
}
