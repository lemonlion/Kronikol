// Fuzz of R2's matcher against the real bar builder, through reflection, as both are internal.
//   Completeness: a bar Build drew for a text must read as that text's, under any keyword, table or doc string.
//   Soundness: a bar must not read as a near miss of its text that published's check refuses. Each one found is printed by kind.
// Usage: Fuzz <path to Kronikol.dll> <iterations> [seed]
using System.Reflection;
using System.Text;

var assembly = Assembly.LoadFrom(args[0]);
var iterations = int.Parse(args[1]);
var seed = args.Length > 2 ? int.Parse(args[2]) : 225;
Console.OutputEncoding = Encoding.UTF8;

var bars = assembly.GetType("Kronikol.PlantUml.StepBarPlantUml")!;
var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
var drawsStep = bars.GetMethod("DrawsStep", flags);
if (drawsStep is null) { Console.WriteLine("this build has no DrawsStep"); return; }
var record = assembly.GetType("Kronikol.Ingestion.InteractionRecord")!;
var delimiter = record.GetMethod("StepDelimiterPlantUml", flags)!;

string Bar(string? keyword, string text, string[][]? table, string? doc) =>
    "\n" + (string)delimiter.Invoke(null, [keyword, text, table, doc])! + "\n\n";
bool Drawn(string marker, string? keyword, string text) => (bool)drawsStep.Invoke(null, [marker, keyword, text, true])!;
var capitaliseRow = assembly.GetType("Kronikol.Reports.StepText")!.GetMethod("Capitalise", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
string Capitalised(string text) => (string?)capitaliseRow.Invoke(null, [text]) ?? text;

var capitalise = assembly.GetType("Kronikol.Reports.StepText")!.GetMethod("CapitaliseIfEnabled", flags)!;
string Marker(string plantUml) => plantUml.Replace('\n', ' ').Trim();
bool Published(string marker, string text)
{
    if (string.IsNullOrWhiteSpace(text)) return true;
    return marker.Contains(text, StringComparison.OrdinalIgnoreCase)
           || marker.Contains((string?)capitalise.Invoke(null, [text]) ?? text, StringComparison.OrdinalIgnoreCase);
}
bool Accepts(string plantUml, string? keyword, string text) => Published(Marker(plantUml), text) || Drawn(Marker(plantUml), keyword, text);

var random = new Random(seed);
string[] words =
[
    "a", "b", "the", "order", "is", "placed", "for", "cash", "card", @"C:\temp\new", @"\", @"\n", @"\\", "<$rows>", "<&star>", "<U+0041>", "<b>bold</b>", "<", ">", "~", "~~wave~~", "~<$x>",
    "%date()", "100%", "%Date()", "%", "**bold**", "__under__", "--strike--", "[[link]]", "{{", "..", "....", "|", "|a|", "=", "#", "*", "…", "wait…", "😀", "\t", "tab\tbed",
    new string('x', 120), new string('y', 300), "<" + new string('z', 200), new string('w', 700), "a,", ",", "item,", "\u2028", "\u0085", "é", "ß", "İ", "ı", "ﬁ", "&amp;", "&#39;", "&", "\"\"", "\"quoted\"",
];
string[] separators = [" ", " ", " ", " ", "  ", ", ", "\n", "\n\n", "", "\r\n", "   "];
string?[] keywords = [null, null, "Given", "When", "Then", "And", "But", "Given that", "Étant donné", "*", "GIVEN", "", @"A\B", "<$k>", "%date()", "Sia dato, che"];

string Text()
{
    var count = random.Next(10) switch { < 5 => random.Next(1, 8), < 8 => random.Next(8, 60), < 9 => random.Next(60, 400), _ => random.Next(400, 3000) };
    var sb = new StringBuilder();
    // A run of one word, to stress the wrapper and the cut, a tenth of the time.
    var only = random.Next(10) == 0 ? words[random.Next(words.Length)] : null;
    for (var i = 0; i < count; i++)
    {
        if (i > 0) sb.Append(only is null ? separators[random.Next(separators.Length)] : " ");
        sb.Append(only ?? words[random.Next(words.Length)]);
    }
    return sb.ToString();
}

string[][]? Table() => random.Next(8) == 0 ? [["a", "b"], ["1", new string('c', random.Next(0, 40))]] : null;
string? Doc() => random.Next(8) == 0 ? "{\n  \"a\": 1\n}" : null;

var failures = new Dictionary<string, (int Count, string Sample)>();
void Note(string kind, string sample)
{
    failures.TryGetValue(kind, out var seen);
    failures[kind] = (seen.Count + 1, seen.Sample ?? sample);
}

string Show(string s) => (s.Length > 170 ? s[..120] + " ... " + s[^40..] : s).Replace("\n", "⏎").Replace("\r", "␍").Replace("\t", "⇥");

var checkedCompleteness = 0;
var checkedSoundness = 0;
for (var n = 0; n < iterations; n++)
{
    var text = Text();
    if (string.IsNullOrWhiteSpace(text)) continue;
    var keyword = keywords[random.Next(keywords.Length)];
    var bar = Bar(keyword, text, Table(), Doc());

    checkedCompleteness++;
    if (!Accepts(bar, keyword, text))
        Note("MISSED its own bar", $"keyword=[{keyword}] text=[{Show(text)}] bar=[{Show(bar.Trim())}]");
    if (!Accepts(bar, null, text))
        Note("row has no keyword, and the bar's is more than a word: " + (keyword is { Length: > 0 } && !keyword.Contains(' ') ? "UNEXPECTED" : "as designed"), $"keyword=[{keyword}] text=[{Show(text)}]");
    if (!Accepts(bar, "Other", text))
        Note("row has another keyword, and the bar's is more than a word: " + (keyword is { Length: > 0 } && !keyword.Contains(' ') ? "UNEXPECTED" : "as designed"), $"keyword=[{keyword}] text=[{Show(text)}]");

    // Near misses: what the first form accepted and published refused.
    var lines = text.Replace("\r", "").Trim().Split('\n');
    var trimmed = text.Replace("\r", "").Trim();
    var miss = new List<(string Kind, string Text)>();
    if (trimmed.Length > 1) miss.Add(("last character changed", trimmed[..^1] + (trimmed[^1] == 'Q' ? 'R' : 'Q')));
    var firstSpace = trimmed.IndexOf(' ');
    if (firstSpace > 0) miss.Add(("a space removed", trimmed.Remove(firstSpace, 1)));
    var aLetter = trimmed.IndexOfAny("abcdefghijklmnopqrstuvwxyz".ToCharArray(), Math.Min(1, trimmed.Length));
    if (aLetter > 0 && trimmed[aLetter - 1] != ' ') miss.Add(("a space added", trimmed.Insert(aLetter, " ")));
    if (lines.Length > 1) miss.Add(("a later line changed", lines[0] + "\nsomething else altogether"));
    miss.Add(("a later line added", trimmed + "\nsomething else altogether"));
    if (trimmed.Length > 450) miss.Add(("the text's end changed past 400", trimmed[..420] + " and then something else"));
    if (firstSpace > 0) miss.Add(("a tail of the text, a word shorter", trimmed[(firstSpace + 1)..]));
    var secondSpace = firstSpace > 0 ? trimmed.IndexOf(' ', firstSpace + 1) : -1;
    if (secondSpace > 0) miss.Add(("a tail of the text, two words shorter", trimmed[(secondSpace + 1)..]));

    // The one letter whose case a step's row and its bar can differ in, and a later one, which they cannot.
    var letters = trimmed.Select((c, i) => (c, i)).Where(p => char.IsLetter(p.c)).Select(p => p.i).ToArray();
    string OtherCase(int at) => trimmed[..at] + (char.IsUpper(trimmed[at]) ? char.ToLowerInvariant(trimmed[at]) : char.ToUpperInvariant(trimmed[at])) + trimmed[(at + 1)..];
    // A row with no keyword is capitalised before the walk reads it. On the text itself, as a keyword's bar keeps a text's leading spaces, which the trimmed copy has lost.
    var capitalised = Capitalised(text);
    if (!Accepts(bar, null, capitalised))
        Note("row capitalised under its bar: " + (keyword is { Length: > 0 } && keyword.Contains(' ') ? "the bar's keyword is more than a word, as designed" : "UNEXPECTED"), $"keyword=[{keyword}] text=[{Show(text)}]");
    // With a keyword nothing changes a text's case, so a row apart by its first letter is a near miss.
    if (!string.IsNullOrWhiteSpace(keyword) && letters.Length > 0) miss.Add(("the first letter's case changed under a keyword", OtherCase(letters[0])));
    if (letters.Length > 1) miss.Add(("a later letter's case changed", OtherCase(letters[1 + random.Next(letters.Length - 1)])));

    foreach (var (kind, other) in miss)
    {
        if (string.IsNullOrWhiteSpace(other) || other == trimmed) continue;
        checkedSoundness++;
        var marker = Marker(bar);
        if (!Published(marker, other) && Drawn(marker, keyword, other))
            Note((marker.EndsWith('…') ? "cut bar, rest unseen: " : string.IsNullOrWhiteSpace(keyword) ? "FALSE on a bar with no keyword: " : "FALSE on a bar with a keyword: ") + kind, $"keyword=[{keyword}] drawn=[{Show(text)}] read as=[{Show(other)}]");
    }
}

Console.WriteLine($"completeness: {checkedCompleteness} bars; soundness: {checkedSoundness} near misses");
foreach (var (kind, (count, sample)) in failures.OrderByDescending(f => f.Value.Count))
    Console.WriteLine($"  {count,6}  {kind}\n          first: {sample}");
if (failures.Count == 0) Console.WriteLine("  no failures");

// The cost of a bar that reads as nothing, at the worst shape the search can meet.
var words16k = string.Join(" ", Enumerable.Repeat("a", 7900));
var bar16k = Bar("Given", words16k, null, null).Trim();
var almost = words16k[..^1] + "b";
var clock = System.Diagnostics.Stopwatch.StartNew();
var refused = Drawn(Marker(bar16k), "Given", almost);
Console.WriteLine($"worst shape: bar of {bar16k.Length} chars, read as a text of {almost.Length} that differs at its end: {clock.Elapsed.TotalMilliseconds:F1} ms, read as {refused}");
