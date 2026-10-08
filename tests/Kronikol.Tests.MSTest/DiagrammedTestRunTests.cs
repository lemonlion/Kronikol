using Microsoft.VisualStudio.TestTools.UnitTesting;
using Kronikol.MSTest;

namespace Kronikol.Tests.MSTest;

[TestClass]
public class DiagrammedTestRunTests
{
    [TestMethod]
    public void TestContexts_ShouldBeInitialized()
    {
        Assert.IsNotNull(DiagrammedTestRun.TestContexts);
    }

    [TestMethod]
    public void TestContexts_ShouldAcceptMSTestScenarioInfo()
    {
        var info = new MSTestScenarioInfo
        {
            TestClassSimpleName = "TestClass",
            TestMethodName = "TestMethod",
            TestId = "TestClass.TestMethod",
            Outcome = UnitTestOutcome.Passed
        };

        DiagrammedTestRun.TestContexts.Enqueue(info);

        // The queue is process-wide: the test classes that derive from DiagrammedComponentTest enqueue their own
        // scenarios at cleanup, so this one looks for its entry rather than taking the first.
        Assert.IsTrue(DiagrammedTestRun.TestContexts.Contains(info));
        Assert.AreEqual("TestClass", DiagrammedTestRun.TestContexts.Single(x => ReferenceEquals(x, info)).TestClassSimpleName);
    }
}
