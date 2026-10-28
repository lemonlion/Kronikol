#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false
// What GraphQlOperationDetector.TryExtractLabel makes of request bodies real clients send, beside the ones its own
// tests cover. Run from this folder: dotnet run --file graphql-detector.cs
using Kronikol.PlantUml;

(string Case, string Body, string Expected)[] cases =
[
    ("named query (covered by the tests)",
        """{"query":"query GetUser { user { name } }"}""", "query GetUser"),
    ("Apollo Client key order: operationName, variables, query",
        """{"operationName":"GetUser","variables":{"id":"1"},"query":"query GetUser($id: ID!) { user(id: $id) { name } }"}""", "query GetUser"),
    ("a variable named query before the top-level query (a search API)",
        """{"operationName":"Search","variables":{"query":"repo:x"},"query":"query Search($query: String!) { search(query: $query) { count } }"}""", "query Search"),
    ("the same, without operationName",
        """{"variables":{"query":"repo:x"},"query":"query Search($query: String!) { search(query: $query) { count } }"}""", "query Search"),
    ("Apollo persisted query, hash only (first try)",
        """{"operationName":"GetUser","variables":{"id":"1"},"extensions":{"persistedQuery":{"version":1,"sha256Hash":"ecf4edb46db40b5132295c0291d62fb65d6759a9eedfa4d5d612dd5ec54a6b38"}}}""", "GetUser"),
    ("a batch of two operations",
        """[{"query":"query A { a }"},{"query":"query B { b }"}]""", "(none: pinned by design)"),
    ("a mutation with an input object holding a key named query",
        """{"variables":{"input":{"query":"x"}},"query":"mutation Save($input: In!) { save(input: $input) { ok } }"}""", "mutation Save"),
];

foreach (var (name, body, expected) in cases)
{
    var label = GraphQlOperationDetector.TryExtractLabel(body);
    Console.WriteLine($"{(label == expected || (label is null && expected.StartsWith("(none")) ? "ok  " : "MISS")} {name}");
    Console.WriteLine($"     got: {label ?? "null"}   expected: {expected}");
}
