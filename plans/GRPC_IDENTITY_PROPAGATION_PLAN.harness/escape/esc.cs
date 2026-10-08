// The plan's section 4.1 rule, prototyped: run with `dotnet run esc.cs` (.NET 10 SDK). For another runtime, put
// `#:property TargetFramework=net8.0` (or net9.0) as the first line of a copy.
const string Prefix = "UTF-8''";

static bool Plain(string v) =>
    v.All(c => c >= (char)0x20 && c <= (char)0x7E)
    && !v.StartsWith(Prefix, StringComparison.Ordinal)
    && (v.Length == 0 || (v[0] != ' ' && v[^1] != ' '));

static string Encode(string v) => Plain(v) ? v : Prefix + Uri.EscapeDataString(v);

static string Decode(string v) =>
    v.StartsWith(Prefix, StringComparison.Ordinal) ? Uri.UnescapeDataString(v[Prefix.Length..]) : v;

static string Show(string v) => v.Length > 60 ? v[..60] + $"... ({v.Length} chars)" : v;

Console.WriteLine($"runtime {Environment.Version}");
var values = new (string Label, string Value)[]
{
    ("ascii name", "Place an order"),
    ("e-acute", "Café order"),
    ("emoji", "Order a coffee ☕"),
    ("xUnit v3 shortened argument", "Theory(text: \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"···)"),
    ("line feed", "Theory(a" + (char)10 + "b)"),
    ("tab", "Theory(a" + (char)9 + "b)"),
    ("leading space", " Place an order"),
    ("trailing space", "Place an order "),
    ("literal prefix", "UTF-8''not encoded"),
    ("empty", ""),
    ("MSTest id", "Tests.Café.Order"),
    ("GUID id", "0f8fad5b-d9cb-469f-a165-70867728950e"),
    ("lone surrogate", "a" + (char)0xD800 + "b"),
    ("70,000 e-acute", new string('é', 70000)),
};
foreach (var (label, value) in values)
{
    var encoded = Encode(value);
    var back = Decode(encoded);
    var printable = encoded.All(c => c >= (char)0x20 && c <= (char)0x7E);
    var unchanged = ReferenceEquals(encoded, value) || encoded == value;
    Console.WriteLine($"{label,-28} unchanged={unchanged,-5} printable={printable,-5} roundtrip={back == value,-5} encoded={Show(encoded)}");
}
Console.WriteLine($"malformed escape: Decode(\"UTF-8''%E9%ZZ\") = {Decode("UTF-8''%E9%ZZ")}");
