using Kronikol.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kronikol.Tests.MSTest;

/// <summary>
/// A class that overloads a test method runs as MSTest runs it. Measured on 4.13.3: <see cref="DiagrammedComponentTest"/>'s
/// <c>[TestCleanup]</c> looked the method up with <c>GetMethod(name)</c>, which throws <c>AmbiguousMatchException</c> for an
/// overloaded name, so both overloads failed, passing or not.
/// </summary>
[TestClass]
public class OverloadedTestMethodTests : DiagrammedComponentTest
{
    private static string Id(string name) => $"{typeof(OverloadedTestMethodTests).FullName}.{name}";

    [TestMethod]
    public void Overloaded()
    {
        Assert.AreEqual(Id(nameof(Overloaded)), CurrentTestInfo.Fetcher().Id);
    }

    [TestMethod]
    [DataRow(1)]
    public void Overloaded(int row)
    {
        Assert.AreEqual(Id($"{nameof(Overloaded)} ({row})"), CurrentTestInfo.Fetcher().Id);
    }
}
