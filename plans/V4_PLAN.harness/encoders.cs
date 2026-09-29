using System.Text.Encodings.Web;
var s = "{\"a\":\"<b> & 'c' +d ✓\"}";
Console.WriteLine("default: " + JavaScriptEncoder.Default.Encode(s));
Console.WriteLine("relaxed: " + JavaScriptEncoder.UnsafeRelaxedJsonEscaping.Encode(s));
