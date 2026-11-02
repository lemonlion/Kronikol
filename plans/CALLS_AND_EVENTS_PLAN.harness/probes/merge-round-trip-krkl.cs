// A report data file as plain JSON text, for a script that compares keys. args: <in> <out.json>. Prints nothing of its content.
#:package Kronikol@5.27.13
#:property PublishAot=false

using System.Reflection;
using System.Text;
using Kronikol.Reports;

const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
var fileFormat = typeof(ReportGenerator).Assembly.GetType("Kronikol.Compression.ReportFileFormat", true)!;
var open = fileFormat.GetMethods(Any).First(m => m.Name == "Open");
using var input = File.OpenRead(args[0]);
var content = open.Invoke(null, [input, open.GetParameters()[1].DefaultValue])!;
var stream = (Stream)content.GetType().GetProperty("Stream")!.GetValue(content)!;
using var reader = new StreamReader(stream, Encoding.UTF8);
File.WriteAllText(args[1], reader.ReadToEnd(), new UTF8Encoding(false));
return 0;
