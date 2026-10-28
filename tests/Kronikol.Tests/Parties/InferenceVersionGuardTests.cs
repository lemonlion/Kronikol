using System.Reflection;
using Kronikol.Constants;
using Kronikol.Parties;

namespace Kronikol.Tests.Parties;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 4.2 and 5.1.6: <see cref="PartyResolver.InferenceVersion"/> says which inference a card's
/// inferred rows came from, so two cards inferred under different rules are not compared by their hash. This holds the
/// inference's answers on a corpus of call sets, one for every dependency category (a palette change that moves a category
/// into or out of a store type changes who can be the subject) and one for each shape the rules decide, keyed by the
/// version: a changed answer fails here until the version moves and the answers are pinned again under it.
/// </summary>
public class InferenceVersionGuardTests
{
    /// <summary>The answers pinned for each version, one line a call set: its name, then every party's kind and source.</summary>
    private static readonly Dictionary<int, string> Pinned = new()
    {
        [1] = """
            category AI: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category AtlasDataApi: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category BigQuery: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category Bigtable: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category BlobStorage: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category ClickHouse: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category CloudStorage: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category CosmosDB: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category Database: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category DynamoDB: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category Elasticsearch: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category gRPC: Caller test/inferred; Other unknown/-; Party underTest/inferred; Service underTest/inferred
            category HTTP: Caller test/inferred; Other unknown/-; Party underTest/inferred; Service underTest/inferred
            category MediatR: Caller test/inferred; Other unknown/-; Party underTest/inferred; Service underTest/inferred
            category MessageQueue: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category MongoDB: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category MySQL: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category Oracle: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category PostgreSQL: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category Redis: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category S3: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category ServiceBus: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category Spanner: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category SQL: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category SQLite: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category SqlServer: Caller test/inferred; Other unknown/-; Party unknown/-; Service underTest/inferred
            category User: Caller test/inferred; Other unknown/-; Party underTest/inferred; Service underTest/inferred
            every call to a party marked a double: Caller test/inferred; Cow double/observed; Service underTest/inferred
            some calls to a party marked a double: Caller test/inferred; Cow unknown/-; Service underTest/inferred
            the test's most called party is a double: Caller test/inferred; Db unknown/-; Service underTest/inferred; Stub double/observed
            nothing records attribution: Db unknown/-; Scheduler test/inferred; Service underTest/inferred; Test test/inferred
            a scheduler calls the service: Caller test/inferred; Job unknown/-; Service underTest/inferred
            a broker delivers to the service: Caller test/inferred; Kafka unknown/-; Service underTest/inferred
            no caller qualifies: Job unknown/-; Service underTest/inferred; Timer test/inferred
            a second subject takes a quarter: Billing underTest/inferred; Caller test/inferred; Db unknown/-; Orders underTest/inferred
            a third party takes less than a quarter: Billing unknown/-; Caller test/inferred; Db unknown/-; Orders underTest/inferred
            the test calls only stores: Cache unknown/-; Caller test/inferred; Db unknown/-
            """
    };

    /// <summary>Every category constant <see cref="DependencyCategories"/> names, by name.</summary>
    private static IEnumerable<string> Categories() =>
        typeof(DependencyCategories).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The corpus. For each category, the test calls a party of that category most, and the service once, and both make
    /// calls: a store category keeps the party out of the subjects, any other makes it the first. Then one call set for each
    /// shape the rules decide.
    /// </summary>
    private static IEnumerable<(string Name, PartyCall[] Calls)> Corpus()
    {
        const string context = "TestContext";
        const string header = "RequestHeader";
        foreach (var category in Categories())
            yield return ($"category {category}",
            [
                new("Caller", "Party", category, null, context),
                new("Caller", "Party", category, null, context),
                new("Caller", "Party", category, null, context),
                new("Caller", "Service", null, null, context),
                new("Party", "Other", null, null, header),
                new("Service", "Other", null, null, header)
            ]);

        yield return ("every call to a party marked a double", [new("Caller", "Service", null, null, context), new("Service", "Cow", null, null, header, "WireMock"), new("Service", "Cow", null, null, header, "WireMock")]);
        yield return ("some calls to a party marked a double", [new("Caller", "Service", null, null, context), new("Service", "Cow", null, null, header, "WireMock"), new("Service", "Cow", null, null, header)]);
        yield return ("the test's most called party is a double", [new("Caller", "Stub", null, null, context, "WireMock"), new("Caller", "Stub", null, null, context, "WireMock"), new("Caller", "Service", null, null, context), new("Service", "Db", "SQL", null, header)]);
        yield return ("nothing records attribution", [new("Test", "Service"), new("Scheduler", "Service"), new("Service", "Db", "SQL")]);
        yield return ("a scheduler calls the service", [new("Caller", "Service", null, null, context), new("Job", "Service", null, null, "Scope"), new("Job", "Service", null, null, "Detached")]);
        yield return ("a broker delivers to the service", [new("Caller", "Service", null, null, context), new("Kafka", "Service", null, "MessageQueue", header), new("Kafka", "Service", "MessageQueue", null, header)]);
        yield return ("no caller qualifies", [new("Job", "Service", null, null, "Scope"), new("Timer", "Service", null, null, "Scope"), new("Timer", "Service", null, null, "TestContext")]);
        yield return ("a second subject takes a quarter", [new("Caller", "Orders", null, null, context), new("Caller", "Orders", null, null, context), new("Caller", "Orders", null, null, context), new("Caller", "Billing", null, null, context), new("Orders", "Db", "SQL", null, header), new("Billing", "Db", "SQL", null, header)]);
        yield return ("a third party takes less than a quarter", [new("Caller", "Orders", null, null, context), new("Caller", "Orders", null, null, context), new("Caller", "Orders", null, null, context), new("Caller", "Orders", null, null, context), new("Caller", "Orders", null, null, context), new("Caller", "Billing", null, null, context), new("Orders", "Db", "SQL", null, header), new("Billing", "Db", "SQL", null, header)]);
        yield return ("the test calls only stores", [new("Caller", "Db", "SQL", null, context), new("Caller", "Cache", "Redis", null, context)]);
    }

    private static string Answer(PartyCall[] calls) =>
        string.Join("; ", PartyResolver.Resolve(calls, PartyDeclarations.None, PartyObservations.None).Parties
            .Select(p => $"{p.Name} {PartiesReport.KindName(p.Kind)}/{(p.KindSource is { } source ? PartiesReport.SourceName(source) : "-")}"));

    [Fact]
    public void The_inferences_answers_are_the_ones_pinned_for_its_version()
    {
        var answers = string.Join("\n", Corpus().Select(c => $"{c.Name}: {Answer(c.Calls)}"));

        Assert.True(Pinned.ContainsKey(PartyResolver.InferenceVersion),
            $"InferenceVersion {PartyResolver.InferenceVersion} has no pinned answers; pin these under it:\n{answers}");
        Assert.True(Pinned[PartyResolver.InferenceVersion].ReplaceLineEndings("\n").Trim() == answers,
            $"The inference answers differently from what version {PartyResolver.InferenceVersion} pinned. Move InferenceVersion "
            + $"and pin the new answers under it (plans/SERVICE_CARD_PLAN.md 4.2):\n{answers}");
    }
}
