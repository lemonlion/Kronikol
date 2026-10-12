using System.Reflection;
using System.Text;

namespace Kronikol.Tracking;

/// <summary>
/// An access chain in an assertion's text: a name, then members, calls and indexes, as in
/// <c>expected.Colour.ToString()</c>, <c>expected.Lines[0]</c> or <c>expected.Inner?.Value</c>. Both value paths read
/// values through it (the weave's, in <see cref="Track"/>, and <c>Track.That</c>'s, in
/// <see cref="ClosureValueResolver"/>), and <see cref="AssertionExpressionFormatter"/> finds the same chains in the
/// text it formats, so a value is put in quotes only in place of exactly the code it was read for (#163).
/// </summary>
/// <remarks>
/// A chain is read the way C# reads it, member by member, up to the first method call or index: a member that a call
/// follows is a method's name and is never read as a property, so <c>Lines.Count(…)</c> is not
/// <c>List&lt;T&gt;.Count</c>. Reading gives a value or nothing: a member that is not there, a getter that throws and
/// a null before a plain <c>.</c> (where the code itself would have thrown) are not the value null. A null before
/// <c>?.</c> is, for the whole rest of the chain, as in C#.
/// </remarks>
internal sealed class AccessChain
{
    internal enum SegmentKind { Member, Call, Index }

    /// <summary>One step of a chain: <c>.Name</c> or <c>?.Name</c>, a call's brackets (with any type arguments before
    /// them) or an index's, <c>?[…]</c> included. <see cref="End"/> is where it ends in the text, exclusive.</summary>
    internal readonly record struct Segment(SegmentKind Kind, string? Name, bool NullConditional, int End);

    private static readonly HashSet<string> OperandKeywords = new(StringComparer.Ordinal)
    {
        "nameof", "typeof", "sizeof", "default",
    };

    private AccessChain(string root, int start, int end, IReadOnlyList<Segment> segments, int readCount, int readEnd,
        string key, string readKey)
    {
        Root = root;
        Start = start;
        End = end;
        Segments = segments;
        ReadCount = readCount;
        ReadEnd = readEnd;
        Key = key;
        ReadKey = readKey;
    }

    /// <summary>The name the chain starts at: a variable, a parameter or a field (after <c>this.</c>, if written).</summary>
    public string Root { get; }

    /// <summary>Where the chain starts in the text it was found in, at <c>this</c> when it has one.</summary>
    public int Start { get; }

    /// <summary>Where the chain ends in the text, exclusive.</summary>
    public int End { get; }

    public IReadOnlyList<Segment> Segments { get; }

    /// <summary>How many segments are read: the members before the first method's name, call or index.</summary>
    public int ReadCount { get; }

    /// <summary>Where the read part ends in the text, exclusive.</summary>
    public int ReadEnd { get; }

    /// <summary>Whether the chain goes on past its read part with a method call or an index.</summary>
    public bool HasUnreadRest => ReadCount < Segments.Count;

    /// <summary>The chain's text as <see cref="CanonicalKey"/> writes it.</summary>
    public string Key { get; }

    /// <summary>The read part's text as <see cref="CanonicalKey"/> writes it.</summary>
    public string ReadKey { get; }

    /// <summary>
    /// Every chain in <paramref name="text"/>, in the order they start: one at each name that is not a member of
    /// something before it, outside string and character literals and outside <c>nameof(…)</c> (whose operand is
    /// not evaluated), with <c>this.field</c> read as the field. A chain inside another's brackets is found too.
    /// </summary>
    public static List<AccessChain> Find(string text)
    {
        var chains = new List<AccessChain>();
        var literal = AssertionExpressionFormatter.LiteralMask(text);
        for (var i = 0; i < text.Length; i++)
        {
            if (literal[i] || !IsIdentifierStart(text[i]) || (i > 0 && (IsIdentifierChar(text[i - 1]) || text[i - 1] == '@')))
                continue;

            var nameEnd = IdentifierEnd(text, i);
            var name = text[i..nameEnd];
            if (IsMemberOfSomething(text, i))
            {
                i = nameEnd - 1;
                continue;
            }

            if (OperandKeywords.Contains(name))
            {
                var open = SkipSpace(text, nameEnd);
                if (open < text.Length && text[open] == '(')
                {
                    var close = Matching(text, literal, open);
                    i = close < 0 ? text.Length : close;
                    continue;
                }
            }

            var root = name;
            var rootEnd = nameEnd;
            if (name == "this")
            {
                var dot = SkipSpace(text, nameEnd);
                var field = dot < text.Length && text[dot] == '.' ? SkipSpace(text, dot + 1) : -1;
                if (field < 0 || field >= text.Length || !IsIdentifierStart(text[field]))
                {
                    i = nameEnd - 1;
                    continue;
                }
                rootEnd = IdentifierEnd(text, field);
                root = text[field..rootEnd];
            }

            chains.Add(Parse(text, literal, i, root, rootEnd));
            i = rootEnd - 1;
        }
        return chains;
    }

    /// <summary>
    /// The text of some code with what does not change its value taken out, so that the code's raw text (which the
    /// readers parse, since <c>?.</c> matters to them) and the label's cleaned text (which the formatter parses)
    /// give the same key: outside literals, whitespace and the null-forgiving <c>!</c> are dropped, and <c>?.</c> and
    /// <c>?[</c> read as <c>.</c> and <c>[</c>.
    /// </summary>
    public static string CanonicalKey(string code)
    {
        var literal = AssertionExpressionFormatter.LiteralMask(code);
        var sb = new StringBuilder(code.Length);
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (!literal[i])
            {
                var next = i + 1 < code.Length ? code[i + 1] : '\0';
                if (char.IsWhiteSpace(c))
                    continue;
                if (c == '!' && i > 0 && (IsIdentifierChar(code[i - 1]) || code[i - 1] is ')' or ']') && next != '=')
                    continue;
                if (c == '?' && next is '.' or '[')
                    continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>A number, an enum, a string, a bool, a <see cref="Guid"/>, a date or a time: a value that reads as
    /// itself, which a label may put before the call made on it (<c>'Blue'.ToString()</c>).</summary>
    public static bool IsScalarType(Type type)
    {
        return type.IsPrimitive || type.IsEnum || type == typeof(string)
            || type == typeof(decimal) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan) || type == typeof(DateOnly)
            || type == typeof(TimeOnly);
    }

    /// <summary>
    /// Reads the chain from its root's value: each member by property, then field, up to the first method's name,
    /// call or index. Reading never runs a method the code called.
    /// </summary>
    public ChainRead Read(object? rootValue)
    {
        var current = rootValue;
        for (var i = 0; i < ReadCount; i++)
        {
            var segment = Segments[i];
            if (current is null)
            {
                return segment.NullConditional
                    ? ChainRead.WholeValue(null)
                    : ChainRead.Unreadable($"null before '.{segment.Name}'");
            }
            if (!TryReadMember(current, segment.Name!, out current, out var reason))
                return ChainRead.Unreadable(reason);
        }

        if (ReadCount == Segments.Count)
            return ChainRead.WholeValue(current);

        // '?.' or '?[' over a null gives the whole rest of the chain the value null, calls included.
        return current is null && Segments[ReadCount].NullConditional
            ? ChainRead.WholeValue(null)
            : ChainRead.PartValue(current);
    }

    private static bool TryReadMember(object target, string name, out object? value, out string reason)
    {
        const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            try
            {
                var property = type.GetProperty(name, flags);
                if (property is not null && property.GetMethod is not null && property.GetIndexParameters().Length == 0)
                {
                    value = property.GetValue(target);
                    reason = "";
                    return true;
                }

                var field = type.GetField(name, flags);
                if (field is not null)
                {
                    value = field.GetValue(target);
                    reason = "";
                    return true;
                }
            }
            catch (Exception ex)
            {
                var thrown = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
                value = null;
                reason = $"reading '{name}' threw {thrown.GetType().Name}";
                return false;
            }
        }

        value = null;
        reason = $"property '{name}' not found on {target.GetType().Name}";
        return false;
    }

    private static AccessChain Parse(string text, bool[] literal, int start, string root, int rootEnd)
    {
        var segments = new List<Segment>();
        var pos = rootEnd;
        while (pos < text.Length)
        {
            // The null-forgiving '!' changes nothing; '!=' is an operator.
            var bang = pos;
            while (bang < text.Length && text[bang] == '!' && !(bang + 1 < text.Length && text[bang + 1] == '='))
                bang++;
            var at = SkipSpace(text, bang);

            var nullConditional = at + 1 < text.Length && text[at] == '?' && text[at + 1] is '.' or '[';
            var opener = nullConditional ? at + 1 : at;
            if (opener >= text.Length || literal[opener])
                break;

            if (text[opener] == '.' && !(opener + 1 < text.Length && text[opener + 1] == '.'))
            {
                var name = SkipSpace(text, opener + 1);
                if (name >= text.Length || !IsIdentifierStart(text[name]))
                    break;
                var nameEnd = IdentifierEnd(text, name);
                segments.Add(new Segment(SegmentKind.Member, text[name..nameEnd], nullConditional, nameEnd));
                pos = nameEnd;
                continue;
            }

            if (text[opener] == '[')
            {
                var close = Matching(text, literal, opener);
                if (close < 0)
                    break;
                segments.Add(new Segment(SegmentKind.Index, null, nullConditional, close + 1));
                pos = close + 1;
                continue;
            }

            if (nullConditional || bang > pos)
                break;

            // A call: its type arguments, written straight after the name, then its brackets.
            var paren = at;
            if (text[pos] == '<' && TypeArgumentsEnd(text, literal, pos) is > 0 and var afterTypes)
                paren = SkipSpace(text, afterTypes);
            if (paren < text.Length && text[paren] == '(' && !literal[paren])
            {
                var close = Matching(text, literal, paren);
                if (close < 0)
                    break;
                segments.Add(new Segment(SegmentKind.Call, null, false, close + 1));
                pos = close + 1;
                continue;
            }
            break;
        }

        var end = segments.Count == 0 ? rootEnd : segments[^1].End;
        var readCount = 0;
        while (readCount < segments.Count && segments[readCount].Kind == SegmentKind.Member &&
               !(readCount + 1 < segments.Count && segments[readCount + 1].Kind == SegmentKind.Call))
            readCount++;
        var readEnd = readCount == 0 ? rootEnd : segments[readCount - 1].End;

        return new AccessChain(root, start, end, segments, readCount, readEnd,
            CanonicalKey(text[start..end]), CanonicalKey(text[start..readEnd]));
    }

    /// <summary>A name preceded by <c>.</c> or <c>?.</c> (whitespace allowed) is a member of what comes before it;
    /// a range's <c>..</c> is not member access.</summary>
    private static bool IsMemberOfSomething(string text, int nameStart)
    {
        var i = nameStart - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
            i--;
        return i >= 0 && text[i] == '.' && !(i > 0 && text[i - 1] == '.');
    }

    /// <summary>The index after a type argument list starting at <paramref name="open"/>, or -1 when what follows the
    /// <c>&lt;</c> is not one (a less-than comparison).</summary>
    private static int TypeArgumentsEnd(string text, bool[] literal, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (literal[i])
                return -1;
            var c = text[i];
            if (c == '<')
                depth++;
            else if (c == '>')
            {
                if (--depth == 0)
                    return i + 1;
            }
            else if (!(IsIdentifierChar(c) || char.IsWhiteSpace(c) || c is '.' or ',' or '?' or '[' or ']' or '(' or ')'))
                return -1;
        }
        return -1;
    }

    /// <summary>The bracket that closes the one at <paramref name="open"/>, counting only brackets outside literals,
    /// or -1.</summary>
    private static int Matching(string text, bool[] literal, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (literal[i])
                continue;
            switch (text[i])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    if (--depth == 0)
                        return i;
                    break;
            }
        }
        return -1;
    }

    private static int SkipSpace(string text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        return i;
    }

    private static int IdentifierEnd(string text, int start)
    {
        var i = start;
        while (i < text.Length && IsIdentifierChar(text[i]))
            i++;
        return i;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}

/// <summary>What reading an <see cref="AccessChain"/> gave: nothing (<see cref="Readable"/> false, with the
/// <see cref="Reason"/>), the whole chain's value, or the value of the part read before a method call or an index.</summary>
internal readonly record struct ChainRead(bool Readable, bool Whole, object? Value, string? Reason)
{
    public static ChainRead WholeValue(object? value) => new(true, true, value, null);

    public static ChainRead PartValue(object? value) => new(true, false, value, null);

    public static ChainRead Unreadable(string reason) => new(false, false, null, reason);
}

/// <summary>
/// The values read for an assertion's text, each under <see cref="AccessChain.CanonicalKey"/> of the code it was read
/// for. A key in <see cref="Prefixes"/> was read up to a method call or an index and holds a single value (a number,
/// an enum, a string, …), so the label may put it before the rest of its chain: <c>'Blue'.ToString()</c> (#163, Q1).
/// </summary>
internal sealed class ChainValues : Dictionary<string, string>
{
    public ChainValues() : base(StringComparer.Ordinal)
    {
    }

    public HashSet<string> Prefixes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Keeps what <paramref name="read"/> gave for <paramref name="chain"/>: the whole chain's value when it prints,
    /// or the read part's, as a prefix, when the chain goes on past it and the value is a single value. Answers
    /// whether a value was kept; <paramref name="format"/> takes the value and the key it would be kept under, and
    /// answers null for a value that does not print.
    /// </summary>
    public bool Keep(AccessChain chain, ChainRead read, Func<object?, string, string?> format)
    {
        if (!read.Readable)
            return false;

        if (read.Whole)
        {
            var whole = format(read.Value, chain.Key);
            return whole is not null && (TryAdd(chain.Key, whole) || ContainsKey(chain.Key));
        }

        if (read.Value is null || !AccessChain.IsScalarType(read.Value.GetType()))
            return false;

        var part = format(read.Value, chain.ReadKey);
        if (part is null)
            return false;
        TryAdd(chain.ReadKey, part);
        Prefixes.Add(chain.ReadKey);
        return true;
    }
}
