// Which origin-form targets make new Uri(authority + target) throw?
string[] targets = ["/%", "/%zz", "/\u0001x", "/\u007fx", "/ä", "/[x]", "/x#frag", "/\\host/x", "//[::1]:80/x", "/" + new string('a', 70000), "/?" + new string('q', 70000), "/x\u00a0y", "/%00"];
foreach (var t in targets)
{
    try { var u = new Uri("http://127.0.0.1:5000" + t); Console.WriteLine($"{Show(t)} -> ok {u.Authority} {Show(u.PathAndQuery)}"); }
    catch (Exception ex) { Console.WriteLine($"{Show(t)} -> {ex.GetType().Name}: {ex.Message}"); }
}
static string Show(string s) => s.Length > 40 ? s[..20] + $"…({s.Length})" : s.Replace("\u0001", "\u0001").Replace("\u007f", "\u007f");
