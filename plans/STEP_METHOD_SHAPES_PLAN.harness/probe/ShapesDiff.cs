// A differential: each row holds one body twice, once with an assertion in it and once with a plain identity in its place, and only the first is woven.
// Any input on which the two differ is a run the weave changed. Conditions cross statement forms, in a plain method under a step and as a value step.
namespace StepShapes;

public static partial class Shapes
{
    private static IEnumerable<Shape> DiffRows()
    {
        var conditions = new (string Id, string Text)[]
        {
            ("gt", "@A(s).Length > 5"),
            ("and", "@A(s) != null && s.Length > 5"),
            ("or", "@A(s).Length > 5 || s == \"a\""),
            ("not", "!(@A(s).Length > 5)"),
            ("pattern", "@A(s) is { Length: > 5 }"),
            ("ternary", "(@A(s).Length > 5 ? s == \"abcdefgh\" : s == \"b\")"),
            ("nullcond", "@A(s)?.Length > 5"),
            ("eqtrue", "(@A(s).Length > 5) == true"),
            ("list", "@A(l).Count > n"),
            ("second", "s != null && @A(s).Length > 5"),
            ("orsecond", "s == null || @A(s).Length > 5"),
            ("two", "@A(s).Length > 5 && @A(s).Length < 100"),
            ("call", "string.IsNullOrEmpty(@A(s))"),
            ("value", "@B(n) > 2"),
            ("cast", "@C(o).Length > 5"),
        };
        var forms = new (string Id, string Body, bool Async)[]
        {
            ("ifelse", "if (@) t += \"T\"; else t += \"E\";", false),
            ("if", "if (@) t += \"T\"; t += \"x\";", false),
            ("ifreturn", "if (@) return t + \"R\"; t += \"x\";", false),
            ("while", "var i = 0; while ((@) && i < 3) { i++; t += \"L\"; }", false),
            ("whilefirst", "var i = 0; while (@) { if (++i > 3) break; t += \"L\"; }", false),
            ("for", "for (var i = 0; (@) && i < 3; i++) t += \"L\";", false),
            ("dowhile", "var k = 0; do { t += \"D\"; k++; } while ((@) && k < 3);", false),
            ("break", "var k = 0; while (true) { if (@) break; t += \"B\"; if (++k > 3) break; }", false),
            ("continue", "foreach (var q in new[] { 1, 2 }) { if (@) continue; t += q; }", false),
            ("finally", "try { if (@) t += \"T\"; else t += \"E\"; } finally { t += \"F\"; }", false),
            ("catch", "try { if (@) throw new ArithmeticException(\"x\"); t += \"N\"; } catch (ArithmeticException) { t += \"C\"; }", false),
            ("incatch", "try { throw new ArithmeticException(\"x\"); } catch (ArithmeticException) { if (@) t += \"T\"; else t += \"E\"; }", false),
            ("infinally", "try { t += \"B\"; } finally { if (@) t += \"T\"; else t += \"E\"; }", false),
            ("when", "try { throw new ArithmeticException(\"x\"); } catch (ArithmeticException) when (@) { t += \"W\"; } catch (ArithmeticException) { t += \"C\"; }", false),
            ("using", "using (new System.IO.MemoryStream()) { if (@) t += \"T\"; else t += \"E\"; }", false),
            ("usingreturn", "using (new System.IO.MemoryStream()) { if (@) return t + \"R\"; } t += \"x\";", false),
            ("lock", "lock (this) { if (@) t += \"T\"; t += \"x\"; }", false),
            ("stored", "var v = (@) ? \"a\" : \"b\"; t += v;", false),
            ("assign", "t += (@) ? \"a\" : \"b\";", false),
            ("switch", "switch ((@) ? 1 : 2) { case 1: t += \"1\"; break; default: t += \"d\"; break; }", false),
            ("switchexpr", "t += ((@) ? 1 : 2) switch { 1 => \"1\", _ => \"d\" };", false),
            ("bool", "var b = @; t += b;", false),
            ("nested", "if (@) { if (s == \"abcdefgh\") t += \"A\"; else t += \"Z\"; } else t += \"E\";", false),
            ("goto", "if (@) goto done; t += \"x\"; done: t += \"y\";", false),
            ("lambda", "Func<bool> f = () => @; t += f();", false),
            ("local", "bool G() => @; t += G();", false),
            ("iterator", "IEnumerable<string> It() { if (@) yield return \"T\"; else yield return \"E\"; } t += string.Concat(It());", false),
            ("returnvalue", "return (@) ? t + \"a\" : t + \"b\";", false),
            ("async", "await Task.Yield(); if (@) t += \"T\"; else t += \"E\"; await Task.Yield();", true),
            ("asyncwhile", "await Task.Yield(); var i = 0; while ((@) && i < 3) { i++; await Task.Yield(); t += \"L\"; }", true),
            ("asyncfinally", "try { await Task.Yield(); if (@) t += \"T\"; else t += \"E\"; } finally { t += \"F\"; }", true),
        };
        const string extra = "public static class K { public static T Keep<T>(T v) => v; public static int KeepInt(int? v) => v!.Value; public static string KeepStr(object o) => (string)o; }\n";
        foreach (var (cid, ctext) in conditions)
        foreach (var (fid, fbody, isAsync) in forms)
        foreach (var kind in new[] { "plain", "step" })
        {
            string Body(bool woven)
            {
                var text = fbody.Replace("@", ctext);
                foreach (var (mark, shouldly, keep) in new[] { ("@A(s)", "s.ShouldNotBeNull()", "K.Keep(s)"), ("@A(l)", "l.ShouldNotBeNull()", "K.Keep(l)"), ("@B(n)", "((int?)n).ShouldNotBeNull()", "K.KeepInt((int?)n)"), ("@C(o)", "o.ShouldBeOfType<string>()", "K.KeepStr(o)") })
                    text = text.Replace(mark, woven ? shouldly : keep);
                return "var t = \"\"; object o = s; " + text + " return t;";
            }
            var ret = isAsync ? "async Task<string>" : "string";
            var parameters = "string s, List<int> l, int n";
            var step = kind == "step" ? "[Step] " : "";
            var members = $"{step}public {ret} Go({parameters}) {{ {Body(true)} }} public {ret} Ctl({parameters}) {{ {Body(false)} }} [Step] public void Drive(Action a) {{ a(); }}";
            string Call(string m) => isAsync ? $"{m}(s, l, n).GetAwaiter().GetResult()" : $"{m}(s, l, n)";
            var goes = kind == "step" ? Call("x.Go") : $"Run(x, s, l, n)";
            var driver = "var x = new Steps(); var l = new List<int> { 1, 2, 3 }; var bad = \"\"; "
                + "string Run(Steps y, string s, List<int> l, int n) { string r = null; y.Drive(() => r = " + (isAsync ? "y.Go(s, l, n).GetAwaiter().GetResult()" : "y.Go(s, l, n)") + "); return r; } "
                + "foreach (var (s, n) in new[] { (\"a\", 1), (\"abcdefgh\", 5), (\"\", 3), (\"abc\", 0) }) { string a, c; "
                + "try { a = " + goes + "; } catch (Exception e) { a = \"threw \" + e.GetType().Name; } "
                + "try { c = " + Call("x.Ctl") + "; } catch (Exception e) { c = \"threw \" + e.GetType().Name; } "
                + "if (a != c) bad += \"[\" + s + \",\" + n + \": \" + a + \" vs \" + c + \"]\"; } "
                + "return bad == \"\" ? \"same\" : bad;";
            yield return S($"D-{cid}-{fid}-{kind}", "D differential", members, driver, value: "same", tree: null, minMs: 0, extra: extra, assertions: true, assertionsFirst: false, noInner: true);
        }
    }
}
