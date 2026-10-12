using System.Diagnostics;

var a = new Activity("a").Start();
var b = new Activity("b").Start();          // child of a, current
Activity.Current = null;
a.Stop();                                   // a is not current
Console.WriteLine($"after a.Stop() while not current: Current = {Activity.Current?.OperationName ?? "null"}");
Activity.Current = b;
Activity.Current = a;                       // a has ended
Console.WriteLine($"after setting Current to the ended a: Current = {Activity.Current?.OperationName ?? "null"}");
b.Stop();                                   // b's parent a has ended
Console.WriteLine($"after b.Stop(): Current = {Activity.Current?.OperationName ?? "null"}, stopped = {Activity.Current?.IsStopped}");
