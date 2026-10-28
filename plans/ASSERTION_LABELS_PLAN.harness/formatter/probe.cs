#:project ../../../src/Kronikol/Kronikol.csproj
#:property TargetFramework=net10.0
// Runs each statement in a corpus through the worktree's AssertionExpressionFormatter.Format with no values, as the
// weave passes a statement whose arguments it did not capture, and prints "statement => label". A "\n" in a corpus
// line stands for a line break, and a label's line breaks print as "\n" (published.cs does the same on 5.12.0).
using Kronikol.Tracking;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var corpus = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "corpus.txt");
foreach (var line in File.ReadAllLines(corpus))
{
    if (line.Length == 0 || line.StartsWith("#"))
        continue;
    var label = AssertionExpressionFormatter.Format(line.Replace("\\n", "\n"));
    Console.WriteLine($"{line}\n    => {label.Replace("\n", "\\n")}");
}
