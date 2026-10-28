using System.Text.RegularExpressions;
using Kronikol.Naming;

namespace Kronikol;

/// <summary>
/// Resolves human-readable scenario and feature titles from test method names and display names.
/// </summary>
/// <remarks>
/// A title made from a method's name is a sentence by one rule (<see cref="SentenceCase"/>): words the identifier writes
/// in capitals keep them, so <c>WhenMCAFundingActivates</c> reads "When MCA funding activates". A name the test wrote (a
/// DisplayName, a <c>TestName</c>) is shown as written. Ids stay as they were: each lane also records the name 5.16.4
/// gave, through the frozen <c>…AsOf5164</c> copies below (plans/TITLES_AS_WRITTEN_PLAN.md 4.4).
/// </remarks>
public static partial class ScenarioTitleResolver
{
    /// <summary>
    /// Detects when a BDDfy scenario title has been set to the class name (e.g. via .BDDfy(nameof(ClassName)))
    /// and replaces it with a sentence made from the test method name.
    /// </summary>
    public static string ResolveScenarioTitle(string scenarioTitle, string? testClassSimpleName, string? testMethodName)
    {
        if (testClassSimpleName is null || testMethodName is null)
            return scenarioTitle;

        if (scenarioTitle != testClassSimpleName)
            return scenarioTitle;

        var sentence = SentenceCase.Sentence(testMethodName);
        return sentence.Length == 0 ? scenarioTitle : sentence;
    }

    private const int MaxParameterLength = 200;

    /// <summary>
    /// Extracts the parameter portion from a test display name (e.g. xUnit Theory's
    /// <c>Ns.Class.Method(param1: "v1", param2: "v2")</c>) and appends it as <c>[param1: "v1", param2: "v2"]</c>.
    /// Returns the original title unchanged when there are no parameters.
    /// </summary>
    public static string AppendTestParameters(string resolvedTitle, string? testDisplayName)
    {
        if (testDisplayName is null)
            return resolvedTitle;

        // As FormatScenarioDisplayName reads them: a framework appends arguments at the end, so a name with a '(' in
        // mid-sentence ("Returns 404 (not found) for a missing order") has none.
        var parenIndex = testDisplayName.IndexOf('(');
        if (parenIndex < 0 || !testDisplayName.EndsWith(')'))
            return resolvedTitle;

        var paramContent = WithoutClosingParenthesis(testDisplayName[(parenIndex + 1)..]);
        if (paramContent.Length == 0)
            return resolvedTitle;

        if (paramContent.Length > MaxParameterLength)
            paramContent = paramContent[..MaxParameterLength] + "…";

        return $"{resolvedTitle} [{paramContent}]";
    }

    /// <summary>
    /// Humanizes a test class simple name (e.g. PascalCase) into a Title Case feature name.
    /// </summary>
    public static string FormatFeatureName(string testClassSimpleName) => testClassSimpleName.Titleize();

    /// <summary>
    /// Parses a test display name (optionally fully-qualified), turns the method name into a sentence, and appends any
    /// parameter values in brackets. <c>Ns.Class.GetHTTPResponse(p: "v")</c> → <c>Get HTTP response [p: "v"]</c>.
    /// </summary>
    /// <remarks>
    /// A display name can also be a sentence (xUnit's <c>[Fact(DisplayName = …)]</c>, MSTest's and TUnit's
    /// display names). Its dots are punctuation, so a namespace is removed only from a method path: no
    /// whitespace, and a last segment that can be a method name. Parameters are what a test framework appends
    /// at the end, so only a name that ends in <c>)</c> has them. No input throws. Kronikol's own lanes call this only
    /// for a name they know to be a method's: one the test wrote is shown as written.
    /// </remarks>
    public static string FormatScenarioDisplayName(string testDisplayName)
    {
        string methodPath;
        string? parameters = null;

        var parenIndex = testDisplayName.IndexOf('(');
        if (parenIndex >= 0 && testDisplayName.EndsWith(')'))
        {
            methodPath = testDisplayName[..parenIndex];
            var paramContent = WithoutClosingParenthesis(testDisplayName[(parenIndex + 1)..]);
            if (paramContent.Length > 0)
            {
                parameters = paramContent.Length > MaxParameterLength
                    ? paramContent[..MaxParameterLength] + "…"
                    : paramContent;
            }
        }
        else
        {
            methodPath = testDisplayName;
        }

        var sentence = SentenceCase.Sentence(MethodName(methodPath));
        if (sentence.Length == 0)
            return testDisplayName.Trim();

        return parameters is not null ? $"{sentence} [{parameters}]" : sentence;
    }

    /// <summary>
    /// Names a scenario on a lane whose default test name is its method's with any arguments after it (TUnit, NUnit,
    /// MSTest): the method's name, or the method's name and then <c>(</c>, <c> (</c> or <c>&lt;</c>, maybe behind a
    /// dotted path with no space in it (NUnit's <c>{C}.{m}</c> templates). Such a name is read by the rule; any other is
    /// one the test wrote and is shown as written. <paramref name="writtenBase"/> is a name the test wrote that the
    /// framework then gives its own arguments (MSTest's data rows under a <c>[TestMethod(DisplayName = …)]</c>).
    /// </summary>
    internal static ScenarioName FormatLaneScenarioName(string name, string methodName, string? writtenBase = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new(FormatScenarioDisplayName(methodName), Written: false, ReadArguments: true);

        if (writtenBase is { Length: > 0 } && name.StartsWith(writtenBase, StringComparison.Ordinal) &&
            LaneArguments(name[writtenBase.Length..]) is { } arguments)
            return new(writtenBase + arguments, Written: true, ReadArguments: arguments.Length > 0);

        return DefaultNameAt(name, methodName) is { } at
            ? new(FormatScenarioDisplayName(name[at..]), Written: false, ReadArguments: true)
            : new(name, Written: true, ReadArguments: false);
    }

    /// <summary>
    /// Names a scenario from an xUnit display name. xUnit builds one from a base: a theory row's own name (v3's
    /// <c>TestDisplayName</c>), else the test's DisplayName, else the class and method formatted by <c>methodDisplay</c>
    /// and <c>methodDisplayOptions</c>; then it appends a row's label in brackets, or its type arguments and its
    /// arguments in parentheses. A base is generated when it is one of <paramref name="defaultNames"/>, the lane's
    /// formatted defaults for the method, each with the method's own part, which the rule reads; any other base is one
    /// the test wrote, shown as written. Either way the arguments follow in brackets, cut at 200 characters.
    /// </summary>
    /// <param name="firstParameterName">
    /// The method's first parameter, which says where a row's arguments start after a row name of its own.
    /// </param>
    internal static ScenarioName FormatXunitScenarioName(string displayName, string? explicitDisplayName,
        IEnumerable<(string Name, string Method)> defaultNames, string? firstParameterName)
    {
        if (explicitDisplayName is { Length: > 0 } && displayName.StartsWith(explicitDisplayName, StringComparison.Ordinal) &&
            XunitArguments(displayName[explicitDisplayName.Length..]) is { } afterDisplayName)
            return new(explicitDisplayName + afterDisplayName, Written: true, ReadArguments: afterDisplayName.Length > 0);

        foreach (var (name, method) in defaultNames)
        {
            if (name.Length == 0 || !displayName.StartsWith(name, StringComparison.Ordinal))
                continue;
            var rest = displayName[name.Length..];
            var generic = rest[..TypeArgumentsLength(rest)];
            if (XunitArguments(rest[generic.Length..]) is not { } arguments)
                continue;
            return XunitGenerated(method, generic, arguments);
        }

        // A class path written otherwise than the lane's defaults (a test case type of another framework's making): still
        // the method's default name when it ends the dotted path that is all there is before it.
        foreach (var method in defaultNames.Select(d => d.Method).Where(m => m.Length > 0).Distinct(StringComparer.Ordinal))
        {
            for (var at = displayName.IndexOf(method, StringComparison.Ordinal); at > 0;
                 at = displayName.IndexOf(method, at + 1, StringComparison.Ordinal))
            {
                var path = displayName[..(at - 1)];
                if (displayName[at - 1] != '.' || path.Any(c => char.IsWhiteSpace(c) || c is '(' or '[' or '<'))
                    continue;
                var rest = displayName[(at + method.Length)..];
                var generic = rest[..TypeArgumentsLength(rest)];
                if (XunitArguments(rest[generic.Length..]) is { } arguments)
                    return XunitGenerated(method, generic, arguments);
            }
        }

        var argumentsAt = XunitRowArgumentsAt(displayName, firstParameterName);
        var rowArguments = XunitArguments(displayName[argumentsAt..]) ?? "";
        return new(displayName[..argumentsAt] + rowArguments, Written: true, ReadArguments: rowArguments.Length > 0);
    }

    /// <summary>A generated xUnit name: the method's part read by the rule, with its type arguments, then what xUnit appended.</summary>
    private static ScenarioName XunitGenerated(string method, string generic, string arguments)
    {
        var sentence = SentenceCase.Sentence(method + generic);
        return new((sentence.Length == 0 ? (method + generic).Trim() : sentence) + arguments, Written: false, ReadArguments: true);
    }

    /// <summary>
    /// What follows a base in an xUnit display name, as the report shows it: nothing; a label, <c> [label]</c>, as it
    /// is; or the arguments, <c>(…)</c>, in brackets. Null when the text is none of these, so the base was not the one
    /// the name began with.
    /// </summary>
    private static string? XunitArguments(string rest)
    {
        if (rest.Length == 0)
            return "";
        if (rest.StartsWith(" [", StringComparison.Ordinal) && rest.EndsWith(']'))
            return rest;
        if (rest[0] != '(' || !rest.EndsWith(')'))
            return null;
        var content = WithoutClosingParenthesis(rest[1..]);
        if (content.Length == 0)
            return "";
        return content.Length > MaxParameterLength ? $" [{content[..MaxParameterLength]}…]" : $" [{content}]";
    }

    /// <summary>The length of the type arguments a generic method's name is followed by, <c>&lt;Int32, List&lt;String&gt;&gt;</c>, or 0.</summary>
    private static int TypeArgumentsLength(string rest)
    {
        if (!rest.StartsWith('<'))
            return 0;
        var depth = 0;
        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i] == '<')
                depth++;
            else if (rest[i] == '>' && --depth == 0)
                return i + 1;
        }
        return 0;
    }

    /// <summary>
    /// Where a row's arguments or label begin after a theory row's own name: the last <c>(firstParameter: </c> of a
    /// name ending in <c>)</c>, else the <c> [</c> that opens a closing label, else the end. A row name can hold
    /// brackets of its own, so the arguments are found by the first parameter's name, which xUnit writes before its
    /// value.
    /// </summary>
    private static int XunitRowArgumentsAt(string displayName, string? firstParameterName)
    {
        if (displayName.EndsWith(')') && firstParameterName is { Length: > 0 } &&
            displayName.LastIndexOf($"({firstParameterName}: ", StringComparison.Ordinal) is > 0 and var at)
            return at;
        if (displayName.EndsWith(']') && displayName.LastIndexOf(" [", StringComparison.Ordinal) is > 0 and var label)
            return label;
        return displayName.Length;
    }

    /// <summary>
    /// What follows a written base on a lane that gives it the framework's own arguments: nothing, or <c>(…)</c>
    /// with or without a space before it, shown in brackets. Null when the text is neither.
    /// </summary>
    private static string? LaneArguments(string rest)
    {
        if (rest.Length == 0)
            return "";
        var arguments = rest.StartsWith(' ') ? rest[1..] : rest;
        if (arguments.Length < 2 || arguments[0] != '(' || !arguments.EndsWith(')'))
            return null;
        var content = WithoutClosingParenthesis(arguments[1..]);
        if (content.Length == 0)
            return "";
        return content.Length > MaxParameterLength ? $" [{content[..MaxParameterLength]}…]" : $" [{content}]";
    }

    /// <summary>
    /// Where the method's default name starts in <paramref name="name"/>, or null when the name is not one: the method's
    /// name, alone or followed by <c>(</c>, <c> (</c> or <c>&lt;</c>, at the start or after a dot of a dotted path that
    /// holds no space. The boundary matters both ways: "Orders are sent to Kafka" is not the default name of a method
    /// <c>Order</c>, nor "Login via PayPal" of <c>Login</c>.
    /// </summary>
    private static int? DefaultNameAt(string name, string methodName)
    {
        if (methodName.Length == 0)
            return null;
        for (var at = 0; at < name.Length; at++)
        {
            if (at > 0)
            {
                // a dotted path: only after a dot, and only while nothing but a path stands before it
                var c = name[at - 1];
                if (char.IsWhiteSpace(c) || c is '(' or '<' or '[')
                    return null;
                if (c != '.')
                    continue;
            }
            if (string.CompareOrdinal(name, at, methodName, 0, methodName.Length) != 0)
                continue;
            var end = at + methodName.Length;
            if (end == name.Length || name[end] is '(' or '<' ||
                (name[end] == ' ' && end + 1 < name.Length && name[end + 1] == '('))
                return at;
        }
        return null;
    }

    /// <summary>
    /// The method name of a method path (<c>Ns.Class.Method</c> gives <c>Method</c>), or the text whole when it
    /// is not one: a sentence, whose dots are punctuation, or a dotted name whose last part cannot name a method.
    /// </summary>
    private static string MethodName(string methodPath)
    {
        var lastDotIndex = methodPath.LastIndexOf('.');
        if (lastDotIndex < 0 || methodPath.Any(char.IsWhiteSpace))
            return methodPath;

        var methodName = methodPath[(lastDotIndex + 1)..];
        return methodName.Length > 0 && (char.IsLetter(methodName[0]) || methodName[0] == '_')
            ? methodName
            : methodPath;
    }

    /// <summary>The arguments without the one <c>)</c> that closes them, so <c>x: Foo()</c> keeps its own.</summary>
    private static string WithoutClosingParenthesis(string text) =>
        text.EndsWith(')') ? text[..^1] : text;

    // ── 5.16.4's rule, frozen ────────────────────────────────────────────────────────────────────────────────────────
    //
    // A scenario's stable id hashes its name, so a title that reads better under the rule above would take a new id and
    // lose its history, its quarantine entry and its links (plans/TITLES_AS_WRITTEN_PLAN.md 3.6, F6). Each lane records
    // the name these copies give, where it differs from the name shown, and the id keys on it (Scenario.StableIdKey).
    // They are 5.16.4's formatters as they were, a rule unchanged since 4.16.3, and must never change again: a change
    // here moves the id of every scenario whose title it touches. Named for the last release before the change, as
    // StringCasing.TitleizeAsOf590 is.

    /// <summary>5.16.4's <see cref="ResolveScenarioTitle"/>, frozen for the ids (see the note above).</summary>
    internal static string ResolveScenarioTitleAsOf5164(string scenarioTitle, string? testClassSimpleName, string? testMethodName)
    {
        if (testClassSimpleName is null || testMethodName is null)
            return scenarioTitle;

        if (scenarioTitle != testClassSimpleName)
            return scenarioTitle;

        var humanized = HumanizeAsOf5164(testMethodName);
        return humanized.Length == 0 ? scenarioTitle : humanized;
    }

    /// <summary>5.16.4's <see cref="FormatScenarioDisplayName"/>, frozen for the ids (see the note above).</summary>
    internal static string FormatScenarioDisplayNameAsOf5164(string testDisplayName)
    {
        string methodPath;
        string? parameters = null;

        var parenIndex = testDisplayName.IndexOf('(');
        if (parenIndex >= 0 && testDisplayName.EndsWith(')'))
        {
            methodPath = testDisplayName[..parenIndex];
            var paramContent = WithoutClosingParenthesisAsOf5164(testDisplayName[(parenIndex + 1)..]);
            if (paramContent.Length > 0)
            {
                parameters = paramContent.Length > MaxParameterLengthAsOf5164
                    ? paramContent[..MaxParameterLengthAsOf5164] + "…"
                    : paramContent;
            }
        }
        else
        {
            methodPath = testDisplayName;
        }

        var humanized = HumanizeAsOf5164(MethodNameAsOf5164(methodPath));
        if (humanized.Length == 0)
            return testDisplayName.Trim();

        return parameters is not null ? $"{humanized} [{parameters}]" : humanized;
    }

    /// <summary>5.16.4's xUnit display name reader, frozen for the ids (see the note above).</summary>
    internal static string FormatXunitDisplayNameAsOf5164(string displayName, string? explicitDisplayName)
    {
        if (explicitDisplayName is { Length: > 0 } && displayName.StartsWith(explicitDisplayName, StringComparison.Ordinal))
            return WithArgumentsAsOf5164(explicitDisplayName, displayName[explicitDisplayName.Length..]);

        // Neither a class nor a method name can hold '(', so the first one opens a row's arguments.
        var argumentsAt = displayName.IndexOf('(');
        var head = argumentsAt < 0 ? displayName : displayName[..argumentsAt];
        return WithArgumentsAsOf5164(XunitMethodPartAsOf5164(head), argumentsAt < 0 ? "" : displayName[argumentsAt..]);
    }

    private static string XunitMethodPartAsOf5164(string head)
    {
        var genericAt = head.IndexOf('<');
        var name = genericAt < 0 ? head : head[..genericAt];
        var generic = genericAt < 0 ? "" : head[genericAt..];
        var dot = name.LastIndexOf('.');
        var comma = name.LastIndexOf(", ", StringComparison.Ordinal);
        var method = dot >= 0 ? name[(dot + 1)..] : comma >= 0 ? name[(comma + 2)..] : name;
        return method + generic;
    }

    private static string WithArgumentsAsOf5164(string title, string arguments)
    {
        var humanized = HumanizeAsOf5164(title);
        if (humanized.Length == 0)
            humanized = title.Trim();

        var content = arguments.StartsWith('(') ? WithoutClosingParenthesisAsOf5164(arguments[1..]) : arguments;
        if (content.Length == 0)
            return humanized;
        if (content.Length > MaxParameterLengthAsOf5164)
            content = content[..MaxParameterLengthAsOf5164] + "…";
        return $"{humanized} [{content}]";
    }

    private const int MaxParameterLengthAsOf5164 = 200;

    private static string MethodNameAsOf5164(string methodPath)
    {
        var lastDotIndex = methodPath.LastIndexOf('.');
        if (lastDotIndex < 0 || methodPath.Any(char.IsWhiteSpace))
            return methodPath;

        var methodName = methodPath[(lastDotIndex + 1)..];
        return methodName.Length > 0 && (char.IsLetter(methodName[0]) || methodName[0] == '_')
            ? methodName
            : methodPath;
    }

    private static string WithoutClosingParenthesisAsOf5164(string text) =>
        text.EndsWith(')') ? text[..^1] : text;

    private static string HumanizeAsOf5164(string name)
    {
        var humanized = SplitPascalCaseAsOf5164(name);
        humanized = humanized.Replace("_", " ");
        humanized = MultipleSpacesRegexAsOf5164().Replace(humanized, " ").Trim();
        return humanized.Length == 0
            ? humanized
            : char.ToUpperInvariant(humanized[0]) + humanized[1..].ToLowerInvariant();
    }

    private static string SplitPascalCaseAsOf5164(string input)
    {
        var result = LowerToUpperRegexAsOf5164().Replace(input, "$1 $2");
        result = UpperSequenceRegexAsOf5164().Replace(result, "$1 $2");
        return result;
    }

    [GeneratedRegex(@"(\p{Ll})(\p{Lu})")]
    private static partial Regex LowerToUpperRegexAsOf5164();

    [GeneratedRegex(@"(\p{Lu}+)(\p{Lu}\p{Ll})")]
    private static partial Regex UpperSequenceRegexAsOf5164();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpacesRegexAsOf5164();
}

/// <summary>
/// A scenario's name as its lane read it: what the report shows, whether a person wrote it, and whether arguments the
/// framework appended are in it, so the lane reads its row's values from it.
/// </summary>
internal readonly record struct ScenarioName(string Shown, bool Written, bool ReadArguments);
