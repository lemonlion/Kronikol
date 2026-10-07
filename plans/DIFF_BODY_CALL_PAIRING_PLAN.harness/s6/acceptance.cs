#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// S6 of plans/DIFF_BODY_CALL_PAIRING_PLAN.md: the built engine's `diff <old> <new> --body ADDR` against the rule as
// research/pairing.py states it, for every address in pairing.py's --design file.
//
//   python ../research/pairing.py OLD.json NEW.json --design design.tsv
//   dotnet run acceptance.cs -- <dir holding Kronikol.dll> OLD.json NEW.json design.tsv out.tsv
//
// Run it from outside the repository (copy it to a scratch folder): a file-based app picks up the
// Directory.Build.props of the folders above it. It loads the engine from the directory given, so the same script
// measures the fix and the 4.6.0 control. The partner is read from the text answer's `+` line, which both builds
// print, or from the "carries no body" refusal, which names it; any other exit 2 is a refusal ("-").
using System.Reflection;
using System.Text.RegularExpressions;

var (engineDir, oldReport, newReport, designPath, outPath) = (args[0], args[1], args[2], args[3], args[4]);
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var candidate = Path.Combine(engineDir, new AssemblyName(e.Name).Name + ".dll");
    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
};
var engine = Assembly.LoadFrom(Path.Combine(engineDir, "Kronikol.dll"));
var run = engine.GetType("Kronikol.Query.QueryCommand")!
    .GetMethod("Run", BindingFlags.Public | BindingFlags.Static, [typeof(IReadOnlyList<string>), typeof(TextWriter), typeof(TextWriter)])!;

var plusAddress = new Regex(@"^\+ .*?\b(s\d+/i\d+)\b", RegexOptions.Multiline);
var noBody = new Regex(@"(s\d+/i\d+) carries no body");
int agree = 0, differ = 0;
var lines = new List<string>();
var differences = new List<string>();
foreach (var row in File.ReadAllLines(designPath))
{
    var parts = row.Split('\t');
    var (address, expected, tier) = (parts[0], parts[1], parts[2]);
    var output = new StringWriter();
    var error = new StringWriter();
    var exit = (int)run.Invoke(null, [new List<string> { "diff", oldReport, newReport, "--body", address }, output, error])!;

    string partner;
    string how;
    if (exit == 0 && plusAddress.Match(output.ToString()) is { Success: true } plus)
        (partner, how) = (plus.Groups[1].Value, "diffed");
    else if (exit == 2 && noBody.Match(error.ToString()) is { Success: true } bodyless)
        (partner, how) = (bodyless.Groups[1].Value, "partner has no body");
    else
        (partner, how) = ("-", $"exit {exit}: {error.ToString().Split('\n')[0].Trim()}");

    lines.Add($"{address}\t{partner}\t{expected}\t{tier}\t{how}");
    if (partner == expected)
        agree++;
    else
    {
        differ++;
        differences.Add($"{address}: engine {partner} ({how}), rule {expected} ({tier})");
    }
}

File.WriteAllLines(outPath, lines);
Console.WriteLine($"engine {engine.GetName().Version} from {engineDir}");
Console.WriteLine($"{agree + differ} addresses: {agree} pair as the rule pairs them, {differ} differ");
foreach (var difference in differences)
    Console.WriteLine("  " + difference);
