using System.Reflection;
using Xunit.Sdk;
using Xunit.v3;

namespace Kronikol.LightBDD.xUnit3;

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
    /// Before 5.27.10 a method with <c>ClassData</c> counted one more than its rows, so a whole run fell short of the
    /// count and wrote no Specifications report, and one with <c>MemberData</c> counted one, as the count asked for its
    /// rows the xUnit v2 way (plans/CLIENT_FLOORS_PLAN.md F33).
    /// </summary>
    internal static int NumberOfTestsOf(MethodInfo method)
    {
        var data = method.GetCustomAttributes().OfType<IDataAttribute>().ToArray();
        return data.Length == 0 ? 1 : data.Sum(attribute => RowsOf(attribute, method));
    }

    // Rows that cannot be read count as one test, as they did before.
    private static int RowsOf(IDataAttribute data, MethodInfo method)
    {
        // xUnit gives a MemberData that names no type the test class before it reads the rows; without one the
        // attribute reads none
        if (data is MemberDataAttributeBase { MemberType: null } member)
            member.MemberType = method.ReflectedType;
        var tracker = new DisposalTracker();
        try
        {
            return data.GetData(method, tracker).AsTask().GetAwaiter().GetResult().Count;
        }
        catch
        {
            return 1;
        }
        finally
        {
            try
            {
                tracker.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // a row that fails to dispose leaves the count as it is
            }
        }
    }
}
