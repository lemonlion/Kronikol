using System.Globalization;
using Io.Cucumber.Messages.Types;
using Kronikol.Ingestion.Cucumber;
using Kronikol.ReqNRoll;
using Reqnroll;
using Reqnroll.Formatters.PayloadProcessing;
using Reqnroll.Formatters.RuntimeSupport;

var recorded = "<tree>/tests/Kronikol.Tests/TestData/ReqNRoll/BackgroundShapes.ndjson";
var envelopes = File.ReadLines(recorded).Select(NdjsonSerializer.Deserialize).ToList();
var pickles = envelopes.Where(e => e.Pickle is not null).Select(e => e.Pickle).ToList();
var fromTheDocument = new Dictionary<string, IReadOnlyList<string>>();
foreach (var document in envelopes.Where(e => e.GherkinDocument is not null).Select(e => e.GherkinDocument))
{
    var own = pickles.Where(pickle => pickle.Uri == document.Uri).ToList();
    var info = new FeatureInfo(CultureInfo.InvariantCulture, "Features", document.Feature.Name, "", ProgrammingLanguage.CSharp, [], new FakeMessages(document, own));
    for (var i = 0; i < own.Count; i++)
        fromTheDocument[own[i].Name] = ExamplesBlockResolver.ResolveBackgroundSteps(info, new ScenarioInfo(own[i].Name, null, [], null!, [], i.ToString(CultureInfo.InvariantCulture)));
}
var scenarios = CucumberFeatureSynthesizer.BuildFromFiles([recorded]).Features.SelectMany(feature => feature.Scenarios).ToList();
Console.WriteLine($"scenarios {scenarios.Count}; with a written Background {fromTheDocument.Values.Count(v => v.Count > 0)}; lane with one {scenarios.Count(s => s.BackgroundSteps is { Length: > 0 })}");
foreach (var s in scenarios.OrderBy(x => x.DisplayName, StringComparer.Ordinal))
{
    var doc = fromTheDocument[s.DisplayName];
    var lane = (s.BackgroundSteps ?? []).Select(st => st.Text).ToArray();
    var same = doc.SequenceEqual(lane);
    Console.WriteLine($"{(same ? "  " : "! ")}{s.DisplayName,-22} doc=[{string.Join("; ", doc)}] lane=[{string.Join("; ", lane)}] steps=[{string.Join("; ", (s.Steps ?? []).Select(st => st.Text))}]");
}
sealed class FakeMessages(GherkinDocument? document, List<Pickle> pickles) : IFeatureLevelCucumberMessages
{
    public bool HasMessages => document is not null;
    public GherkinDocument GherkinDocument => document!;
    public IEnumerable<Pickle> Pickles => pickles;
    public Source Source => null!;
}
