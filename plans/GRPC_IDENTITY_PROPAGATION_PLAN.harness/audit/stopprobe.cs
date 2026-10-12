using System.Diagnostics;
var src = new ActivitySource("probe");
ActivitySource.AddActivityListener(new ActivityListener { ShouldListenTo = _ => true, Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData });
string N(Activity? a) => a?.OperationName ?? "null";

// Case 1: started under X, Current restored to X, then Y started, then A stopped while Y is current.
var x = src.StartActivity("X");
var caller = Activity.Current;
var a = src.StartActivity("A");
Activity.Current = caller;
var y = src.StartActivity("Y");
Console.WriteLine($"case1 before Stop: current={N(Activity.Current)}");
a!.Stop();
Console.WriteLine($"case1 after A.Stop while Y current: current={N(Activity.Current)} (Y stopped? {y!.IsStopped})");
y.Stop(); x!.Stop(); Activity.Current = null;

// Case 2: started with no current, restored to null, disposed later in a flow where Z is current.
var a2 = src.StartActivity("A2");
Activity.Current = null;
var z = src.StartActivity("Z");
a2!.Stop();
Console.WriteLine($"case2 after A2.Stop while Z current: current={N(Activity.Current)}");
z!.Stop(); Activity.Current = null;

// Case 3: started under X3, X3 stopped later, then A3 stopped in a flow with no current.
var x3 = src.StartActivity("X3");
var a3 = src.StartActivity("A3");
Activity.Current = x3;
x3!.Stop();
Console.WriteLine($"case3 after X3.Stop: current={N(Activity.Current)}");
a3!.Stop();
Console.WriteLine($"case3 after A3.Stop: current={N(Activity.Current)} stopped={Activity.Current?.IsStopped}");
Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
