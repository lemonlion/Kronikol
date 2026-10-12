using System.Text.RegularExpressions;

namespace Kronikol;

/// <summary>
/// Resolves human-readable scenario and feature titles from test method names and display names.
/// </summary>
public static partial class ScenarioTitleResolver
{
    /// <summary>
    /// Detects when a BDDfy scenario title has been set to the class name (e.g. via .BDDfy(nameof(ClassName)))
    /// and replaces it with a humanized version of the test method name.
    /// </summary>
    public static string ResolveScenarioTitle(string scenarioTitle, string? testClassSimpleName, string? testMethodName)
    {
        if (testClassSimpleName is null || testMethodName is null)
            return scenarioTitle;

        if (scenarioTitle != testClassSimpleName)
            return scenarioTitle;

        var humanized = Humanize(testMethodName);
        return humanized.Length == 0 ? scenarioTitle : humanized;
    }

    /// <summary>
    /// Extracts the parameter portion from a test display name (e.g. xUnit Theory's
    /// <c>Ns.Class.Method(param1: "v1", param2: "v2")</c>) and appends it as <c>[param1: "v1", param2: "v2"]</c>.
    /// Returns the original title unchanged when there are no parameters.
    /// </summary>
    private const int MaxParameterLength = 200;

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
            paramContent = paramContent[..MaxParameterLength] + "\u2026";

        return $"{resolvedTitle} [{paramContent}]";
    }

    /// <summary>
    /// Humanizes a test class simple name (e.g. PascalCase) into a Title Case feature name.
    /// </summary>
    public static string FormatFeatureName(string testClassSimpleName) => testClassSimpleName.Titleize();

    /// <summary>
    /// Parses a test display name (optionally fully-qualified), humanizes the method name,
    /// and appends any parameter values in brackets.
    /// <c>Ns.Class.MyTestMethod(p: "v")</c> → <c>My test method [p: "v"]</c>.
    /// </summary>
    /// <remarks>
    /// A display name can also be a sentence (xUnit's <c>[Fact(DisplayName = …)]</c>, MSTest's and TUnit's
    /// display names). Its dots are punctuation, so a namespace is removed only from a method path: no
    /// whitespace, and a last segment that can be a method name. Parameters are what a test framework appends
    /// at the end, so only a name that ends in <c>)</c> has them. No input throws.
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
                    ? paramContent[..MaxParameterLength] + "\u2026"
                    : paramContent;
            }
        }
        else
        {
            methodPath = testDisplayName;
        }

        var humanized = Humanize(MethodName(methodPath));
        if (humanized.Length == 0)
            return testDisplayName.Trim();

        return parameters is not null ? $"{humanized} [{parameters}]" : humanized;
    }

    /// <summary>
    /// Names a scenario from an xUnit display name, given the DisplayName the test set, if any. xUnit builds a default
    /// name from the class and method and formats it whole by <c>methodDisplayOptions</c> (so a namespace can carry
    /// spaces, or commas for its dots), uses a DisplayName as written, and appends a theory row's arguments to either.
    /// So the method is the part after the namespace whatever its spaces, and a DisplayName keeps its own parentheses.
    /// </summary>
    internal static string FormatXunitDisplayName(string displayName, string? explicitDisplayName)
    {
        if (explicitDisplayName is { Length: > 0 } && displayName.StartsWith(explicitDisplayName, StringComparison.Ordinal))
            return WithArguments(explicitDisplayName, displayName[explicitDisplayName.Length..]);

        // Neither a class nor a method name can hold '(', so the first one opens a row's arguments.
        var argumentsAt = displayName.IndexOf('(');
        var head = argumentsAt < 0 ? displayName : displayName[..argumentsAt];
        return WithArguments(XunitMethodPart(head), argumentsAt < 0 ? "" : displayName[argumentsAt..]);
    }

    /// <summary>
    /// The method of a formatted <c>Class.Method</c>: after the last <c>.</c>, or after the last <c>, </c> when
    /// <c>replacePeriodWithComma</c> made the dots commas, leaving a generic method's <c>&lt;…&gt;</c> with it.
    /// </summary>
    private static string XunitMethodPart(string head)
    {
        var genericAt = head.IndexOf('<');
        var name = genericAt < 0 ? head : head[..genericAt];
        var generic = genericAt < 0 ? "" : head[genericAt..];
        var dot = name.LastIndexOf('.');
        var comma = name.LastIndexOf(", ", StringComparison.Ordinal);
        var method = dot >= 0 ? name[(dot + 1)..] : comma >= 0 ? name[(comma + 2)..] : name;
        return method + generic;
    }

    private static string WithArguments(string title, string arguments)
    {
        var humanized = Humanize(title);
        if (humanized.Length == 0)
            humanized = title.Trim();

        var content = arguments.StartsWith('(') ? WithoutClosingParenthesis(arguments[1..]) : arguments;
        if (content.Length == 0)
            return humanized;
        if (content.Length > MaxParameterLength)
            content = content[..MaxParameterLength] + "…";
        return $"{humanized} [{content}]";
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

    /// <summary>Splits PascalCase and underscores into words, then upper-cases the first letter and lower-cases the rest.</summary>
    private static string Humanize(string name)
    {
        var humanized = SplitPascalCase(name);
        humanized = humanized.Replace("_", " ");
        humanized = MultipleSpacesRegex().Replace(humanized, " ").Trim();
        // Invariant, so a name and its stableId are one on every machine: Turkish and Azeri case 'i' otherwise (until 4.16.3).
        return humanized.Length == 0
            ? humanized
            : char.ToUpperInvariant(humanized[0]) + humanized[1..].ToLowerInvariant();
    }

    private static string SplitPascalCase(string input)
    {
        var result = LowerToUpperRegex().Replace(input, "$1 $2");
        result = UpperSequenceRegex().Replace(result, "$1 $2");
        return result;
    }

    [GeneratedRegex(@"(\p{Ll})(\p{Lu})")]
    private static partial Regex LowerToUpperRegex();

    [GeneratedRegex(@"(\p{Lu}+)(\p{Lu}\p{Ll})")]
    private static partial Regex UpperSequenceRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpacesRegex();
}
