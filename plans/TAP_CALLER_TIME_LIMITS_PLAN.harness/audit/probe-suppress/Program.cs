var outer = ExecutionContext.SuppressFlow();
var before = ExecutionContext.IsFlowSuppressed();
using (ExecutionContext.SuppressFlow())
{
}
var after = ExecutionContext.IsFlowSuppressed();
try { outer.Undo(); Console.WriteLine($"{Environment.Version}: before={before} afterInner={after} outerUndo=ok now={ExecutionContext.IsFlowSuppressed()}"); }
catch (Exception ex) { Console.WriteLine($"{Environment.Version}: before={before} afterInner={after} outerUndo threw {ex.GetType().Name}: {ex.Message}"); }
