using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Kronikol.NUnit4;

/// <summary>Which NUnit test, if any, is running on this flow.</summary>
internal static class RunningTest
{
    /// <summary>
    /// The test running on this flow, or null when none is. NUnit always answers with some context: in a fixture's
    /// constructor or one-time set-up or tear-down, and in a set-up fixture, its current test is the fixture, a suite with an
    /// id of its own; on a thread that did not flow a test's execution context it makes an ad hoc context, with a new id for
    /// each such flow. No scenario owns either id, so a call logged under one appears in no report.
    /// </summary>
    internal static TestContext.TestAdapter? Current
    {
        get
        {
            var context = TestExecutionContext.CurrentContext;
            if (context is TestExecutionContext.AdhocContext || context.CurrentTest is not { IsSuite: false })
                return null;
            return TestContext.CurrentContext.Test;
        }
    }
}
