using System.Reflection;
using Xunit.Sdk;

namespace Kronikol.LightBDD.xUnit2;

internal static class AssemblyExtensions
{
    public static int CountNumberOfTestsInAssembly(this Assembly assembly)
    {
        return assembly.GetTypes()
            .SelectMany(t => t.GetMethods().AsParallel())
            .Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "ScenarioAttribute"))
            .Sum(NumberOfTestsOf);
    }

    /// <summary>
    /// The tests xUnit runs a scenario method as: one for each row of its test data, whichever data attribute gives the
    /// rows (<c>InlineData</c>, <c>MemberData</c>, <c>ClassData</c> or a library's own), and one when it has none.
    /// Before 5.27.10 a method with <c>MemberData</c> or <c>ClassData</c> counted one more than its rows, so a whole run
    /// fell short of the count and wrote no Specifications report (plans/CLIENT_FLOORS_PLAN.md F33).
    /// </summary>
    internal static int NumberOfTestsOf(MethodInfo method)
    {
        var data = method.GetCustomAttributes<DataAttribute>().ToArray();
        return data.Length == 0 ? 1 : data.Sum(attribute => RowsOf(attribute, method));
    }

    // Rows that cannot be read count as one test, as they did before.
    private static int RowsOf(DataAttribute data, MethodInfo method)
    {
        try
        {
            return data.GetData(method)?.Count() ?? 1;
        }
        catch
        {
            return 1;
        }
    }
}
