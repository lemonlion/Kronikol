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

    [TestMethod]
    public void Sums()
    {
        Assert.AreEqual(Id(nameof(Sums)), CurrentTestInfo.Fetcher().Id);
    }

    [TestMethod]
    [DataRow(1, 2, 3)]
    public void Sums(params int[] values)
    {
        // The row passes three values to one params parameter, so no overload takes as many: it was read as the test
        // without data, and given that test's id.
        Assert.AreNotEqual(Id(nameof(Sums)), CurrentTestInfo.Fetcher().Id);
        Assert.AreEqual(typeof(int[]), TestMethodOf(GetType(), TestContext)!.GetParameters().Single().ParameterType);
    }

    [TestMethod]
    [DataRow(1)]
    public void Places(int quantity)
    {
        Assert.AreEqual(typeof(int), TestMethodOf(GetType(), TestContext)!.GetParameters().Single().ParameterType);
    }

    [TestMethod]
    [DataRow("one")]
    public void Places(string quantity)
    {
        // Two overloads take one argument: the one whose parameter takes the row's value, where the first was taken.
        Assert.AreEqual(typeof(string), TestMethodOf(GetType(), TestContext)!.GetParameters().Single().ParameterType);
    }
}
