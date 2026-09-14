using System.Reflection;
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Object;

namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// Base class for Jint-based JavaScript tests.
/// Loads the real advanced-search.js from the embedded resource into a Jint engine.
/// </summary>
public abstract class JintTestBase : IDisposable
{
    protected Engine JsEngine { get; }

    protected JintTestBase()
    {
        JsEngine = new Engine();
        var js = LoadAdvancedSearchJs();
        JsEngine.Execute(js);
    }

    private static string LoadAdvancedSearchJs()
    {
        var assembly = typeof(Reports.ReportGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("advanced-search.js", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded resource advanced-search.js not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    protected bool CallIsAdvancedSearch(string input)
    {
        return JsEngine.Invoke("isAdvancedSearch", input).AsBoolean();
    }

    protected List<TokenResult> CallTokenise(string input)
    {
        var result = JsEngine.Invoke("advancedSearchTokenise", input);
        var arr = result.AsArray();
        var tokens = new List<TokenResult>();

        for (uint i = 0; i < arr.Length; i++)
        {
            var obj = arr[i].AsObject();
            var type = obj.Get("type").AsString();
            var valueProp = obj.Get("value");
            var value = valueProp.IsUndefined() ? null : valueProp.AsString();
            tokens.Add(new TokenResult(type, value));
        }

        return tokens;
    }

    protected object? CallParse(string input)
    {
        // First tokenise, then parse
        var tokens = JsEngine.Invoke("advancedSearchTokenise", input);
        var result = JsEngine.Invoke("advancedSearchParse", tokens);

        if (result.IsNull()) return null;
        return ConvertAstNode(result.AsObject());
    }

    protected bool? CallMatch(string input, string searchText, string[] tags, string status, string[]? verdicts = null)
    {
        // Build a JS Set from the tags array
        JsEngine.SetValue("__tags", tags);
        JsEngine.Execute("var __tagSet = new Set(__tags.map(function(t) { return t.toLowerCase(); }));");
        SetVerdicts(verdicts);

        var result = JsEngine.Invoke("advancedSearchMatch", input.ToLowerInvariant(), searchText.ToLowerInvariant(), JsEngine.GetValue("__tagSet"), status, JsEngine.GetValue("__verdictSet"));

        if (result.IsNull()) return null;
        return result.AsBoolean();
    }

    protected bool CallEvaluate(object ast, string searchText, string[] tags, string status, string[]? verdicts = null)
    {
        // Serialize AST back to JS
        var jsAst = SerializeAstToJs(ast);
        JsEngine.SetValue("__tags", tags);
        JsEngine.Execute("var __tagSet = new Set(__tags.map(function(t) { return t.toLowerCase(); }));");
        SetVerdicts(verdicts);
        JsEngine.Execute($"var __ast = {jsAst};");

        var result = JsEngine.Invoke("advancedSearchEvaluate",
            JsEngine.GetValue("__ast"),
            searchText.ToLowerInvariant(),
            JsEngine.GetValue("__tagSet"),
            status,
            JsEngine.GetValue("__verdictSet"));

        return result.AsBoolean();
    }

    /// <summary>The verdict set the page passes when the report carries history, and null when it does not.</summary>
    private void SetVerdicts(string[]? verdicts)
    {
        if (verdicts is null)
        {
            JsEngine.Execute("var __verdictSet = null;");
            return;
        }
        JsEngine.SetValue("__verdicts", verdicts);
        JsEngine.Execute("var __verdictSet = new Set(__verdicts.map(function(v) { return v.toLowerCase(); }));");
    }

    private static string SerializeAstToJs(object ast)
    {
        return ast switch
        {
            AstText t => $"{{type:'text',value:'{Escape(t.Value)}'}}",
            AstPhrase p => $"{{type:'phrase',value:'{Escape(p.Value)}'}}",
            AstTag t => $"{{type:'tag',value:'{Escape(t.Value)}'}}",
            AstStatus s => $"{{type:'status',value:'{Escape(s.Value)}'}}",
            AstAnd a => $"{{type:'and',left:{SerializeAstToJs(a.Left)},right:{SerializeAstToJs(a.Right)}}}",
            AstOr o => $"{{type:'or',left:{SerializeAstToJs(o.Left)},right:{SerializeAstToJs(o.Right)}}}",
            AstNot n => $"{{type:'not',operand:{SerializeAstToJs(n.Operand)}}}",
            _ => "null"
        };
    }

    private static string Escape(string s) => s.Replace("'", "\\'");

    private static object ConvertAstNode(ObjectInstance obj)
    {
        var type = obj.Get("type").AsString();
        return type switch
        {
            "text" => new AstText(obj.Get("value").AsString()),
            "phrase" => new AstPhrase(obj.Get("value").AsString()),
            "tag" => new AstTag(obj.Get("value").AsString()),
            "status" => new AstStatus(obj.Get("value").AsString()),
            "and" => new AstAnd(ConvertAstNode(obj.Get("left").AsObject()), ConvertAstNode(obj.Get("right").AsObject())),
            "or" => new AstOr(ConvertAstNode(obj.Get("left").AsObject()), ConvertAstNode(obj.Get("right").AsObject())),
            "not" => new AstNot(ConvertAstNode(obj.Get("operand").AsObject())),
            _ => throw new InvalidOperationException($"Unknown AST node type: {type}")
        };
    }

    public void Dispose() => JsEngine.Dispose();
}

public record TokenResult(string Type, string? Value);

// AST node types for C#-side assertion
public record AstText(string Value);
public record AstPhrase(string Value);
public record AstTag(string Value);
public record AstStatus(string Value);
public record AstAnd(object Left, object Right);
public record AstOr(object Left, object Right);
public record AstNot(object Operand);
