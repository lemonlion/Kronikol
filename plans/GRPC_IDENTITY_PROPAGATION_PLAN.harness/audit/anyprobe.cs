#:package Google.Protobuf@3.33.0
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
var inner = new Duration { Seconds = 5 };
var packed = Any.Pack(new Struct { Fields = { ["a"] = Value.ForString("b") } });
var plain = new Struct { Fields = { ["k"] = Value.ForNumber(1), ["s"] = Value.ForString("x") } };
var ts = Timestamp.FromDateTime(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc));
foreach (IMessage m in new IMessage[] { plain, ts, inner, new Empty() })
    Console.WriteLine($"{m.Descriptor.Name}: same={JsonFormatter.Default.Format(m) == JsonFormatter.ToDiagnosticString(m)} [{JsonFormatter.Default.Format(m)}]");
try { Console.WriteLine("Format(Any): " + JsonFormatter.Default.Format(packed)); } catch (Exception e) { Console.WriteLine("Format(Any) threw " + e.GetType().Name + ": " + e.Message); }
Console.WriteLine("Diag(Any): " + JsonFormatter.ToDiagnosticString(packed));
var holder = new Struct { Fields = { ["x"] = Value.ForString("y") } };
