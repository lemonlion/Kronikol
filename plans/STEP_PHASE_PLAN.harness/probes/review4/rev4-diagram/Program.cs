using System.Text;
using Kronikol.PlantUml;
using Kronikol.Tracking;

var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? ".";
Directory.CreateDirectory(dir);
var o = new StringBuilder();

const string T = "test-1";
static Uri U(string p) => new($"http://shop.local/{p}");

static (RequestResponseLog Req, RequestResponseLog Res) Call(string path, string body = "body", bool variants = false, bool skipInSetup = false)
{
    var trace = Guid.NewGuid();
    var id = Guid.NewGuid();
    var req = new RequestResponseLog("Test name", T, "GET", body, U(path), [], "Shop", "Probe", RequestResponseType.Request, trace, id, false) { Timestamp = DateTimeOffset.UtcNow };
    var res = new RequestResponseLog("Test name", T, "GET", body, U(path), [], "Shop", "Probe", RequestResponseType.Response, trace, id, false, System.Net.HttpStatusCode.OK) { Timestamp = DateTimeOffset.UtcNow };
    if (variants)
    {
        req.SetupVariant = new PhaseVariant("GET", U(path), "REQ-SETUP-VARIANT", [], skipInSetup);
        req.ActionVariant = new PhaseVariant("GET", U(path), "REQ-ACTION-VARIANT", [], false);
        res.SetupVariant = new PhaseVariant("GET", U(path), "RES-SETUP-VARIANT", [], skipInSetup);
        res.ActionVariant = new PhaseVariant("GET", U(path), "RES-ACTION-VARIANT", [], false);
    }
    return (req, res);
}

static RequestResponseLog Mark() => new(T, T, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
{
    IsActionStart = true,
    MarkerKind = DiagramMarkerKind.Phase
};

void Show(string name, IEnumerable<RequestResponseLog> logs, bool separate, bool collapse, int maxEncoded = PlantUmlCreator.DefaultMaxEncodedDiagramLength, int? cap = null)
{
    o.AppendLine($"## {name} (separateSetup={separate}, collapse={collapse}, maxEncoded={maxEncoded}, cap={cap})");
    foreach (var r in PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs.ToList(), separateSetup: separate, collapseConsecutiveIdenticalCalls: collapse, maxEncodedDiagramLength: maxEncoded, maxArrowsPerDiagram: cap, lazyLoadImages: false))
    {
        var i = 0;
        foreach (var d in r.PlantUmls)
        {
            o.AppendLine($"  diagram {++i}:");
            var inNote = false;
            foreach (var raw in d.PlainText.Replace("\r", "").Split('\n'))
            {
                var t = raw.Trim();
                if (t.StartsWith("note")) { inNote = true; continue; }
                if (t == "end note") { inNote = false; continue; }
                if (inNote) { if (t.Contains("VARIANT")) o.AppendLine("      note: " + t); continue; }
                if (t.StartsWith("partition")) o.AppendLine("    partition Setup");
                else if (t == "end" || t.StartsWith("loop") || t.Contains("->") || t.StartsWith("hnote")) o.AppendLine("    " + (t.Length > 90 ? t[..90] : t));
            }
        }
        if (i == 0) o.AppendLine("  (no diagram)");
    }
    o.AppendLine();
}

IEnumerable<RequestResponseLog> Flat(params object[] items)
{
    foreach (var item in items)
    {
        if (item is RequestResponseLog l) yield return l;
        else if (item is ValueTuple<RequestResponseLog, RequestResponseLog> c) { yield return c.Item1; yield return c.Item2; }
    }
}

// D1: a run of identical calls right after the boundary, with phase variants, nothing partitioned.
Show("D1 S | M | A A B variants", Flat(Call("s", variants: true), Mark(), Call("a", variants: true), Call("a", variants: true), Call("b", variants: true)), separate: false, collapse: true);
Show("D1s same, a Setup variant that skips", Flat(Call("s", variants: true), Mark(), Call("a", variants: true, skipInSetup: true), Call("a", variants: true, skipInSetup: true), Call("b", variants: true, skipInSetup: true)), separate: false, collapse: true);
Show("D1c control: no collapse", Flat(Call("s", variants: true), Mark(), Call("a", variants: true), Call("a", variants: true), Call("b", variants: true)), separate: false, collapse: false);

// D2: a run that crosses the boundary.
Show("D2 S S | M | S S variants", Flat(Call("p", variants: true), Call("p", variants: true), Mark(), Call("p", variants: true), Call("p", variants: true)), separate: false, collapse: true);

// D3: the whole action folds into the setup's run.
Show("D3 S | M | S variants", Flat(Call("p", variants: true), Mark(), Call("p", variants: true)), separate: false, collapse: true);

// D4: the boundary moved to the end, the diagram split at the last call.
var big = new string('x', 6000);
Show("D4 big | M | big identical (patch: marker last)", Flat(Call("p", big), Mark(), Call("p", big)), separate: false, collapse: true, maxEncoded: 1500);
Show("D4c big | M at the end (both)", Flat(Call("p", big), Mark()), separate: false, collapse: false, maxEncoded: 1500);

// D5: a marker that is not the last still ends a run under SeparateSetup.
Show("D5 S | M | S | M | A", Flat(Call("p"), Mark(), Call("p"), Mark(), Call("a")), separate: true, collapse: true);

// D6: an arrow cap and a held boundary.
Show("D6 S1 S2 | M | A1 A2 cap 2", Flat(Call("s1"), Call("s2"), Mark(), Call("a1"), Call("a2")), separate: false, collapse: true, cap: 2);
Show("D6v S1 | M | A1 A2 cap 2, variants", Flat(Call("s1", variants: true), Mark(), Call("a1", variants: true), Call("a2", variants: true)), separate: false, collapse: true, cap: 2);

// D7: nothing but the marker(s).
Show("D7 M alone", Flat(Mark()), separate: true, collapse: false);
Show("D7b M M alone held", Flat(Mark(), Mark()), separate: false, collapse: true);

// D8: two markers, variants read by position.
Show("D8 S | M | A1 | M | A2 variants", Flat(Call("s", variants: true), Mark(), Call("a1", variants: true), Mark(), Call("a2", variants: true)), separate: true, collapse: false);
Show("D8n same without partition", Flat(Call("s", variants: true), Mark(), Call("a1", variants: true), Mark(), Call("a2", variants: true)), separate: false, collapse: false);

File.WriteAllText(Path.Combine(dir, "rev4-diagram.txt"), o.ToString());
