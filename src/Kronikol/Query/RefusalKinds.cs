namespace Kronikol.Query;

/// <summary>
/// What a refusal of <c>kronikol query</c> was, as <c>error.kind</c> names it in the <c>--json</c> envelope (#184 R3), so
/// a program tells "that is not in the report", "there is no report" and "you used me wrong" apart without reading
/// prose. The exit codes do not change with it: three of these share exit 2 and two share exit 1, and moving a
/// refusal to a new code would change what an existing script sees.
/// </summary>
internal static class RefusalKinds
{
    public const string Usage = "usage";
    public const string NotFound = "notFound";
    public const string Ambiguous = "ambiguous";
    public const string NoReport = "noReport";
    public const string Unsupported = "unsupported";
    public const string Unreadable = "unreadable";
    public const string WriteFailed = "writeFailed";

    /// <summary>The kind a refusal that names none takes from its exit code: 1 is a read that failed, anything else a usage error.</summary>
    public static string ByExit(int exit) => exit == 1 ? Unreadable : Usage;
}

public static partial class QueryCommand
{
    /// <summary>
    /// Writes a refusal's first line and records its kind, the first one this call made winning, as the envelope's
    /// <c>message</c> is its first line. A refusal written straight to the error writer takes its exit code's kind.
    /// </summary>
    internal static void Refuse(TextWriter error, string kind, string message)
    {
        if (_refusalKind is null)
        {
            _refusalKind = kind;
            _refusalMessage = message;
        }
        error.WriteLine(message);
    }

    /// <summary>The first refusal's kind and line so far, which a lookup that turns out not to be this call's puts back.</summary>
    internal static (string? Kind, string? Message) RefusalSoFar
    {
        get => (_refusalKind, _refusalMessage);
        set => (_refusalKind, _refusalMessage) = value;
    }
}
