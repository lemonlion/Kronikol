using System.Text;
using System.Text.Json;

namespace Kronikol.Compression;

/// <summary>
/// Which keys the report's JSON schema requires, wherever they stand: walked beside the JSON a compressed report holds, so
/// that a required key is written even when it is null or empty and every other empty key is left out
/// (plans/REPORT_FORMAT_PLAN.md 4.2). Read from the schema's own <c>required</c> lists at every depth, never typed out.
/// </summary>
internal sealed class RequiredKeys
{
    private RequiredKeys(Node root) => Root = root;

    /// <summary>The schema node of the document's root.</summary>
    public Node Root { get; }

    /// <summary>
    /// Reads <paramref name="schema"/>: <c>properties</c>, <c>items</c> and <c>additionalProperties</c>, references into
    /// <c>definitions</c> or <c>$defs</c> (a recursive definition is one node), and the branches of <c>oneOf</c>,
    /// <c>anyOf</c> and <c>allOf</c>, whose requirements are joined: a key one branch requires is kept.
    /// </summary>
    public static RequiredKeys FromSchema(JsonElement schema)
    {
        // A copy of its own, so the caller may dispose the document it came from.
        schema = schema.Clone();
        var definitions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var name in new[] { "definitions", "$defs" })
        {
            if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty(name, out var section) && section.ValueKind == JsonValueKind.Object)
            {
                foreach (var definition in section.EnumerateObject())
                    definitions[$"#/{name}/{definition.Name}"] = definition.Value;
            }
        }

        var built = new Dictionary<string, Node>(StringComparer.Ordinal);
        return new RequiredKeys(Build(schema, definitions, built));
    }

    /// <summary>A schema with no requirements anywhere: every empty key is left out.</summary>
    public static RequiredKeys None { get; } = new(new Node());

    private static Node Build(JsonElement schema, Dictionary<string, JsonElement> definitions, Dictionary<string, Node> built)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var reference) && reference.GetString() is { } target)
        {
            if (built.TryGetValue(target, out var known))
                return known;
            var node = new Node();
            built[target] = node;
            if (definitions.TryGetValue(target, out var definition))
                Fill(node, definition, definitions, built);
            return node;
        }

        var fresh = new Node();
        Fill(fresh, schema, definitions, built);
        return fresh;
    }

    private static void Fill(Node node, JsonElement schema, Dictionary<string, JsonElement> definitions, Dictionary<string, Node> built)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return;

        if (schema.TryGetProperty("$ref", out _))
        {
            node.Join(Build(schema, definitions, built));
            return;
        }

        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var key in required.EnumerateArray())
            {
                if (key.GetString() is { } name)
                    node.AddRequired(name);
            }
        }

        if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
                node.AddProperty(property.Name, Build(property.Value, definitions, built));
        }

        if (schema.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
            node.Items = Build(items, definitions, built);

        if (schema.TryGetProperty("additionalProperties", out var additional))
        {
            if (additional.ValueKind == JsonValueKind.Object)
                node.Additional = Build(additional, definitions, built);
            else if (additional.ValueKind == JsonValueKind.True)
                node.Additional = Node.Open;
        }

        // An object the schema leaves undescribed, deliberately (internalFlowSegments: rendering data a merge carries
        // through unchanged, which another version may have written): what it holds is kept exactly as written.
        if (IsObjectType(schema) && !Describes(schema))
            node.IsOpen = true;

        foreach (var combinator in new[] { "oneOf", "anyOf", "allOf" })
        {
            if (schema.TryGetProperty(combinator, out var branches) && branches.ValueKind == JsonValueKind.Array)
            {
                foreach (var branch in branches.EnumerateArray())
                {
                    var joined = new Node();
                    Fill(joined, branch, definitions, built);
                    node.Join(joined);
                }
            }
        }
    }

    private static bool IsObjectType(JsonElement schema) =>
        schema.TryGetProperty("type", out var type)
        && (type.ValueKind == JsonValueKind.String && type.ValueEquals("object")
            || type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(t => t.ValueEquals("object")));

    private static bool Describes(JsonElement schema) =>
        new[] { "properties", "additionalProperties", "patternProperties", "items", "$ref", "oneOf", "anyOf", "allOf" }
            .Any(keyword => schema.TryGetProperty(keyword, out _));

    /// <summary>One place in the schema: the keys it requires, and the nodes its properties, items and map values stand in.</summary>
    internal sealed class Node
    {
        private readonly List<byte[]> _required = [];
        private readonly List<(byte[] Name, Node Child)> _properties = [];
        private readonly List<Node> _joined = [];
        private Node? _items;

        /// <summary>The node everything inside an open part stands in: every key in it is written, as it came.</summary>
        internal static Node Open { get; } = new() { IsOpen = true };

        /// <summary>Whether the schema leaves what this node holds undescribed, so that it is kept exactly as written.</summary>
        internal bool IsOpen { get; set; }

        /// <summary>Whether this node requires <paramref name="utf8Name"/>, given unescaped. Inside an open part, every key.</summary>
        public bool Requires(ReadOnlySpan<byte> utf8Name)
        {
            if (IsOpen)
                return true;
            foreach (var name in _required)
            {
                if (utf8Name.SequenceEqual(name))
                    return true;
            }
            foreach (var joined in _joined)
            {
                if (joined.Requires(utf8Name))
                    return true;
            }
            return false;
        }

        /// <summary>The node a property's value stands in, or null where the schema says nothing of it.</summary>
        public Node? Property(ReadOnlySpan<byte> utf8Name)
        {
            if (IsOpen)
                return Open;
            foreach (var (name, child) in _properties)
            {
                if (utf8Name.SequenceEqual(name))
                    return child;
            }
            foreach (var joined in _joined)
            {
                if (joined.Property(utf8Name) is { } found)
                    return found;
            }
            return Additional ?? FirstJoined(n => n.Additional);
        }

        /// <summary>The node an array's elements stand in, or null where the schema says nothing of them.</summary>
        public Node? Items
        {
            get => IsOpen ? Open : _items ?? FirstJoined(n => n.Items);
            internal set => _items = value;
        }

        /// <summary>The node a map's values stand in (<c>additionalProperties</c>), where a key is not one of its properties.</summary>
        internal Node? Additional { get; set; }

        private Node? FirstJoined(Func<Node, Node?> select)
        {
            foreach (var joined in _joined)
            {
                if (select(joined) is { } found)
                    return found;
            }
            return null;
        }

        internal void AddRequired(string name) => _required.Add(Encoding.UTF8.GetBytes(name));

        internal void AddProperty(string name, Node child) => _properties.Add((Encoding.UTF8.GetBytes(name), child));

        internal void Join(Node other)
        {
            if (!ReferenceEquals(other, this))
                _joined.Add(other);
        }
    }
}
