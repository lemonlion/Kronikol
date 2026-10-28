extern alias K;
using System.Reflection;
using System.Text;
using Xunit;

// ─── Shims the detector's test file binds to (it is compiled in as it is; see route.csproj) ───────────────────────────

namespace Kronikol.PlantUml
{
    /// <summary>Stands in for the library's detector in the test file: answers with the real one, and records every body it is given.</summary>
    public static class GraphQlOperationDetector
    {
        public static string? TryExtractLabel(string? content)
        {
            var label = K::Kronikol.PlantUml.GraphQlOperationDetector.TryExtractLabel(content);
            RouteProbe.Recorder.Body(content, label);
            return label;
        }

        internal static bool IsAtTopLevel(string content, int position)
        {
            RouteProbe.Recorder.NotABody();
            return K::Kronikol.PlantUml.GraphQlOperationDetector.IsAtTopLevel(content, position);
        }
    }
}

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class FactAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TheoryAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InlineDataAttribute(params object?[]? data) : Attribute
    {
        // [InlineData(null)] passes a null array: one null argument, as xUnit reads it.
        public object?[] Data { get; } = data ?? [null];
    }

    /// <summary>Records what each test expected instead of asserting it, so a wrong extraction shows as a mismatch.</summary>
    public static class Assert
    {
        public static void Null(object? actual) => RouteProbe.Recorder.Expect(null, actual);
        public static void Equal<T>(T expected, T actual) => RouteProbe.Recorder.Expect(expected, actual);
        public static void True(bool condition) => RouteProbe.Recorder.Expect(true, condition);
        public static void False(bool condition) => RouteProbe.Recorder.Expect(false, condition);
    }
}

// ─── The runner ──────────────────────────────────────────────────────────────────────────────────────────────────────

namespace RouteProbe
{
    static class Recorder
    {
        public sealed class Case
        {
            public string Test = "";
            public int Row;
            public int Rows;
            public readonly List<(string? Body, string? Label)> Bodies = [];
            public readonly List<(object? Expected, object? Actual)> Expectations = [];
            public bool NotABody;
            public string? Error;
        }

        public static Case? Current;

        public static void Body(string? content, string? label) => Current?.Bodies.Add((content, label));

        public static void NotABody()
        {
            if (Current is not null)
                Current.NotABody = true;
        }

        public static void Expect(object? expected, object? actual) => Current?.Expectations.Add((expected, actual));
    }

    static class Corpus
    {
        public static int Run(string outFile)
        {
            var o = new StringBuilder();
            var type = typeof(Kronikol.Tests.PlantUml.GraphQlOperationDetectorTests);
            var cases = new List<Recorder.Case>();
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(m => m.MetadataToken))
            {
                var isFact = method.GetCustomAttribute<FactAttribute>() is not null;
                var isTheory = method.GetCustomAttribute<TheoryAttribute>() is not null;
                if (!isFact && !isTheory)
                    continue;
                var rows = isTheory ? method.GetCustomAttributes<InlineDataAttribute>().Select(r => r.Data).ToList() : [[]];
                for (var row = 0; row < rows.Count; row++)
                {
                    var c = new Recorder.Case { Test = method.Name, Row = row, Rows = rows.Count };
                    Recorder.Current = c;
                    try
                    {
                        method.Invoke(Activator.CreateInstance(type), rows[row]);
                    }
                    catch (TargetInvocationException e)
                    {
                        c.Error = e.InnerException?.GetType().Name + ": " + e.InnerException?.Message;
                    }
                    finally
                    {
                        Recorder.Current = null;
                    }
                    cases.Add(c);
                }
            }

            var bodyCases = cases.Where(c => c.Bodies.Count > 0).ToList();
            var failing = cases.Where(c => c.Error is not null || c.Expectations.Any(e => !Equals(e.Expected, e.Actual))).ToList();
            o.AppendLine("# The operation key's GraphQL step over GraphQlOperationDetectorTests.cs");
            o.AppendLine($"test cases run {cases.Count} (methods {cases.Select(c => c.Test).Distinct().Count()}); cases passing a body to TryExtractLabel {bodyCases.Count}, " +
                         $"bodies {bodyCases.Sum(c => c.Bodies.Count)} ({bodyCases.SelectMany(c => c.Bodies).Select(b => b.Body ?? "\0null").Distinct().Count()} distinct); " +
                         $"IsAtTopLevel cases (no body) {cases.Count(c => c.NotABody)}");
            o.AppendLine($"each case's own expectation met by the real detector (extraction check): {cases.Count - failing.Count} of {cases.Count}" +
                         (failing.Count == 0 ? "" : "; NOT met: " + string.Join(", ", failing.Select(f => $"{f.Test}[{f.Row}] {f.Error}"))));
            o.AppendLine();
            o.AppendLine("Columns: test[row] | body (excerpt) | detector label | plan key (4.2 as written) | strict key, where it differs | how");
            o.AppendLine("(the plan key is what 4.2's rule appends to the path; '-' is none)");

            int sameAsLabel = 0, planDiffers = 0, strictDiffers = 0;
            var differs = new List<string>();
            foreach (var c in bodyCases)
                foreach (var (body, label) in c.Bodies)
                {
                    var test = c.Rows > 1 ? $"{c.Test}[{c.Row}]" : c.Test;
                    if (body is null || string.IsNullOrWhiteSpace(body) || Key.FirstNonWhite(body) != '{')
                    {
                        // Not a JSON object: the key never reads it as GraphQL (4.4: a batch or anything else stays index-only).
                        var plain = body is null ? null : Gql.Plan(body);
                        o.AppendLine($"  {Cut.To(test, 52)} | {Excerpt(body, 44)} | {label ?? "-"} | - (not a JSON object{(plain?.Operation is null ? "" : "; Plan() would say " + plain.Operation)})");
                        sameAsLabel += label is null ? 1 : 0;
                        continue;
                    }
                    var plan = Gql.Plan(body);
                    var strict = Gql.Strict(body);
                    if (plan.Operation == label)
                        sameAsLabel++;
                    else
                        planDiffers++;
                    var strictNote = strict.Operation == plan.Operation ? "" : $" | STRICT: {strict.Operation ?? "-"}";
                    if (strict.Operation != plan.Operation)
                    {
                        strictDiffers++;
                        differs.Add($"  {Cut.To(test, 52)} | plan {plan.Operation ?? "-"} ({plan.How}) | strict {strict.Operation ?? "-"} ({strict.How})");
                    }
                    o.AppendLine($"  {Cut.To(test, 52)} | {Excerpt(body, 44)} | {label ?? "-"} | {Cut.To(plan.Operation ?? "-", 60)}{strictNote} | {plan.How}");
                }
            o.AppendLine();
            o.AppendLine($"bodies where the plan key is the detector label unchanged {sameAsLabel}; where the plan key differs from the label {planDiffers} (anonymous operations named by root fields); strict differs from plan {strictDiffers}");
            foreach (var d in differs)
                o.AppendLine(d);
            o.AppendLine();

            Probes(o);
            File.WriteAllText(outFile, o.ToString());
            Console.WriteLine(o.ToString());
            return failing.Count == 0 ? 0 : 1;
        }

        /// <summary>Bodies and URIs not in the test file, each for one question the corpus does not ask.</summary>
        private static void Probes(StringBuilder o)
        {
            const string apq = "\"extensions\":{\"persistedQuery\":{\"version\":1,\"sha256Hash\":\"ecf4edb46db40b5132295c0291d62fb65d6759a9eedfa4d5d612dd5ec54a6b38\"}}";
            var probes = new (string Id, string Method, string Uri, string? Body, string Question)[]
            {
                ("P1", "POST", "/graphql", """{"query":"fragment F on Query { a } query Q { ...F }"}""", "a document whose fragments come first (Relay, codegen)"),
                ("P2", "POST", "/graphql", """{"query":"# fetch the user\nquery Q { user { id } }"}""", "a document opening with a comment"),
                ("P3", "POST", "/graphql", """{"query":"query\nGetUser { user { name } }"}""", "a name on the line after its keyword"),
                ("P4", "POST", "/graphql", """{"query":"query A { a } mutation B { b }","operationName":"B"}""", "operationName selects a mutation after a query"),
                ("P5", "POST", "/graphql", """{"variables":{"query":"pancakes"},"query":"query Search($query: String) { search(q: $query) { id } }"}""", "a variable named query before the query (F4)"),
                ("P6", "POST", "/graphql", """{"operationName":"GetUser","variables":{},""" + apq + "}", "APQ by hash alone"),
                ("P7", "POST", "/graphql", """{"operationName":"GetUser","query":"query GetUser { user { name } }",""" + apq + "}", "APQ's registering retry of the same operation"),
                ("P8", "GET", "/graphql?query=%7B%20user%20%7B%20name%20%7D%20%7D", null, "a GET with an anonymous query"),
                ("P9", "GET", "/graphql?operationName=GetUser&variables=%7B%7D&extensions=%7B%22persistedQuery%22%3A%7B%22version%22%3A1%2C%22sha256Hash%22%3A%22abc%22%7D%7D", null, "a GET persisted query (Apollo's GET mode)"),
                ("P10", "GET", "/search?query=mutation%20testing", null, "a REST search whose term starts with a keyword"),
                ("P11", "GET", "/search?query=pancakes&page=2", null, "a REST search"),
                ("P12", "POST", "/search", """{"query":"{\"match\":{\"title\":\"x\"}}"}""", "a search API taking a JSON filter as a string"),
                ("P13", "POST", "/_search", """{"query":{"match":{"title":"x"}}}""", "Elasticsearch's query DSL"),
                ("P14", "POST", "/docs", """{"query":"SELECT * FROM c WHERE c.id = @id","parameters":[{"name":"@id","value":"1"}]}""", "a Cosmos DB query spec posted over HTTP"),
                ("P15", "POST", "/graphql", """{"query":"query Q { a } query R { b }"}""", "two named operations, no operationName (a server rejects it)"),
                ("P16", "POST", "/graphql", """{"query":"{ a(s: \"}\") b }"}""", "a brace inside a string argument"),
                ("P17", "POST", "/graphql", "{\"query\":\"{ a(s: \\\"\\\"\\\"block } ) \\\\\\\"\\\"\\\" still\\\"\\\"\\\") b }\"}", "a block string argument holding braces and an escaped triple quote"),
                ("P18", "POST", "/graphql", """{"query":"query ($id: ID!) { user(id: $id) { name } }"}""", "an anonymous operation with variables"),
                ("P19", "POST", "/graphql", """{"query":"{ ... @include(if: true) { a } b }"}""", "an inline fragment with no type condition"),
                ("P20", "POST", "/graphql", """{"query":"query Q @cached(ttl: 5) { a }"}""", "a directive on the operation"),
                ("P21", "POST", "/graphql", """{"query":"{ __typename }"}""", "the Jest stand-in's anonymous body"),
                ("P22", "POST", "/graphql", """{"query":"mutation { first: createOrder(input: {a: 1}) { id } second: createOrder(input: {a: 2}) { id } }"}""", "one root field twice under two aliases"),
                ("P23", "POST", "/graphql", """{"query":"{ b a }"}""", "the same fields as P24 in the other order"),
                ("P24", "POST", "/graphql", """{"query":"{ a b }"}""", "the same fields as P23"),
                ("P25", "POST", "/graphql", """{"query":"{ orderSummaries(where: {status: {eq: \"x\"}}) { orderId } }","operationName":null}""", "operationName null"),
                ("P26", "POST", "/graphql", """{"query":"query { orders { id } }","operationName":"ListOrders"}""", "a client name for an unnamed operation"),
                ("P27", "POST", "/graphql", """{"query":"query GetUser { user { name } }","variables":{"note":"cut at capture …truncated (""", "a body cut at capture"),
                ("P28", "PUT", "/graphql", """{"query":"mutation M { m }"}""", "a PUT: 4.4 reads POST bodies only"),
            };

            o.AppendLine("# Probes (not in the test file): route = Key.Route(method, uri, no category, body)");
            o.AppendLine("id | method uri | body (excerpt) | plan route | strict route, where it differs | how (plan) | question");
            foreach (var p in probes)
            {
                var plan = Key.Route(p.Method, p.Uri, null, p.Body, GqlRule.Plan, out var planTrace);
                var strict = Key.Route(p.Method, p.Uri, null, p.Body, GqlRule.Strict, out var strictTrace);
                var label = p.Body is null ? null : K::Kronikol.PlantUml.GraphQlOperationDetector.TryExtractLabel(p.Body);
                o.AppendLine($"  {p.Id} | {p.Method} {Cut.To(p.Uri, 40)} | {Excerpt(p.Body, 40)} | detector {label ?? "-"} | plan {Cut.To(plan, 48)}"
                             + (strict == plan ? "" : $" | STRICT {Cut.To(strict, 44)} ({strictTrace.Gql?.How})")
                             + $" | {planTrace.Gql?.How ?? "not read"} | {p.Question}");
            }
            o.AppendLine();

            // 4.1 moves history's input from the escaped PathAndQuery to the data file's string: does {bin} still fire?
            o.AppendLine("# {bin} probe: a key with bytes, as RedisTracker builds the URI (new Uri($\"redis://{endpoint}/{db}/{key}\"))");
            o.AppendLine("key | history today: Template(Uri.PathAndQuery) | the plan's input: Template(PathOf(Uri.ToString())) | Uri.ToString() path");
            foreach (var key in new[] { "app:_customerDates_100000000000001-\u001d(�4��:", "app:_customerDates_100000000000002-(�4��:", "cache/key-ab\u000ccd/next" })
            {
                var uri = new Uri($"redis://localhost:6379/0/{key}");
                var today = K::Kronikol.History.InteractionShape.Template(uri.PathAndQuery);
                var planned = K::Kronikol.History.InteractionShape.Template(Key.PathOf(uri.ToString()));
                o.AppendLine($"  {Visible(key)} | {Visible(today)} | {Visible(planned)} | {Visible(Key.PathOf(uri.ToString()))}");
            }
            o.AppendLine();

            // The SQL trackers' method and table come from UnifiedSqlClassifier; EF Core's SQL Server batches open with two SETs.
            o.AppendLine("# SQL classifier probe: UnifiedSqlClassifier.Classify (operation, table) and the statement head");
            foreach (var sql in new[]
                     {
                         "INSERT INTO [Reservations] ([Status]) OUTPUT INSERTED.[Id] VALUES (@p0);",
                         "SET NOCOUNT ON;\r\nINSERT INTO [Reservations] ([Status]) OUTPUT INSERTED.[Id] VALUES (@p0);",
                         "SET IMPLICIT_TRANSACTIONS OFF;\r\nSET NOCOUNT ON;\r\nINSERT INTO [Reservations] ([Status]) OUTPUT INSERTED.[Id] VALUES (@p0);",
                         "SET IMPLICIT_TRANSACTIONS OFF;\r\nSET NOCOUNT ON;\r\nDELETE FROM [InventoryItems]\r\nOUTPUT 1\r\nWHERE [Id] = @p0;",
                     })
            {
                var info = K::Kronikol.Sql.UnifiedSqlClassifier.Classify(sql);
                o.AppendLine($"  {Cut.To(Visible(sql), 70)} | {info.Operation} {info.TableName ?? "(no table)"} | head: {Key.Head(sql)}");
            }
            o.AppendLine();
        }

        private static string Excerpt(string? body, int n) => body is null ? "(null)" : Cut.To(Visible(body), n);

        private static string Visible(string text)
        {
            var b = new StringBuilder(text.Length);
            foreach (var c in text)
                b.Append(c < ' ' || c == '�' ? $"\\u{(int)c:x4}" : c.ToString());
            return b.ToString();
        }
    }
}
