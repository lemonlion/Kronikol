using Microsoft.VisualStudio.TestTools.UnitTesting;
using Kronikol.MSTest;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.MSTest;

[TestClass]
public class MSTestScenarioInfoToFeaturesTests
{
    [TestMethod]
    public void ShouldGroupScenariosByTestClassSimpleName()
    {
        var infos = new[]
        {
            CreateScenarioInfo("FeatureA", "Test1"),
            CreateScenarioInfo("FeatureA", "Test2"),
            CreateScenarioInfo("FeatureB", "Test1")
        };

        var features = infos.ToFeatures();

        Assert.AreEqual(2, features.Length);
        Assert.AreEqual("Feature A", features[0].DisplayName);
        Assert.AreEqual("Feature B", features[1].DisplayName);
    }

    [TestMethod]
    public void ShouldReplaceUnderscoresWithSpacesInFeatureDisplayName()
    {
        var infos = new[] { CreateScenarioInfo("My_Feature_Name", "Test1") };

        var features = infos.ToFeatures();

        Assert.AreEqual("My Feature Name", features[0].DisplayName);
    }

    [TestMethod]
    public void ShouldReplaceUnderscoresWithSpacesInScenarioDisplayName()
    {
        var infos = new[] { CreateScenarioInfo("Feature", "Given_a_request_When_called_Then_returns") };

        var features = infos.ToFeatures();

        Assert.AreEqual("Given a request when called then returns", features[0].Scenarios[0].DisplayName);
    }

    [TestMethod]
    public void ShouldSetEndpointFromScenarioInfo()
    {
        var infos = new[] { CreateScenarioInfo("Feature", "Test1", endpoint: "/api/cake") };

        var features = infos.ToFeatures();

        Assert.AreEqual("/api/cake", features[0].Endpoint);
    }

    [TestMethod]
    public void ShouldSetEndpointToNullWhenNotProvided()
    {
        var infos = new[] { CreateScenarioInfo("Feature", "Test1") };

        var features = infos.ToFeatures();

        Assert.IsNull(features[0].Endpoint);
    }

    [TestMethod]
    public void ShouldOrderHappyPathScenariosFirst()
    {
        var infos = new[]
        {
            CreateScenarioInfo("Feature", "AAA_NonHappy", isHappyPath: false),
            CreateScenarioInfo("Feature", "ZZZ_HappyPath", isHappyPath: true)
        };

        var features = infos.ToFeatures();

        Assert.AreEqual("Zzz happy path", features[0].Scenarios[0].DisplayName);
        Assert.IsTrue(features[0].Scenarios[0].IsHappyPath);
        Assert.AreEqual("Aaa non happy", features[0].Scenarios[1].DisplayName);
        Assert.IsFalse(features[0].Scenarios[1].IsHappyPath);
    }

    [TestMethod]
    public void ShouldMapPassedOutcomeCorrectly()
    {
        var infos = new[] { CreateScenarioInfo("Feature", "Test1", outcome: UnitTestOutcome.Passed) };

        var features = infos.ToFeatures();

        Assert.AreEqual(ExecutionResult.Passed, features[0].Scenarios[0].Result);
    }

    [TestMethod]
    public void ShouldMapFailedOutcomeCorrectly()
    {
        var infos = new[] { CreateScenarioInfo("Feature", "Test1", outcome: UnitTestOutcome.Failed, errorMessage: "Assertion failed") };

        var features = infos.ToFeatures();

        Assert.AreEqual(ExecutionResult.Failed, features[0].Scenarios[0].Result);
        Assert.AreEqual("Assertion failed", features[0].Scenarios[0].ErrorMessage);
    }

    [TestMethod]
    public void ShouldDeduplicateByTestId()
    {
        var infos = new[]
        {
            CreateScenarioInfo("Feature", "Test1", testId: "Feature.Test1"),
            CreateScenarioInfo("Feature", "Test1", testId: "Feature.Test1")
        };

        var features = infos.ToFeatures();

        Assert.AreEqual(1, features[0].Scenarios.Length);
    }

    [TestMethod]
    public void ShouldSetTestIdOnScenario()
    {
        var infos = new[] { CreateScenarioInfo("Feature", "Test1", testId: "Namespace.Feature.Test1") };

        var features = infos.ToFeatures();

        Assert.AreEqual("Namespace.Feature.Test1", features[0].Scenarios[0].Id);
    }

    [TestMethod]
    public void ShouldIncludeErrorStackTrace()
    {
        var infos = new[]
        {
            CreateScenarioInfo("Feature", "Test1", outcome: UnitTestOutcome.Failed,
                errorMessage: "Assert.AreEqual failed",
                errorStackTrace: "at MyTest.cs:line 42")
        };

        var features = infos.ToFeatures();

        Assert.AreEqual("at MyTest.cs:line 42", features[0].Scenarios[0].ErrorStackTrace);
    }

    [TestMethod]
    public void ShouldOrderFeaturesAlphabetically()
    {
        var infos = new[]
        {
            CreateScenarioInfo("Zebra_Feature", "Test1"),
            CreateScenarioInfo("Apple_Feature", "Test1")
        };

        var features = infos.ToFeatures();

        Assert.AreEqual("Apple Feature", features[0].DisplayName);
        Assert.AreEqual("Zebra Feature", features[1].DisplayName);
    }

    [TestMethod]
    public void ShouldOrderNonHappyPathScenariosByMethodNameAlphabetically()
    {
        var infos = new[]
        {
            CreateScenarioInfo("Feature", "Charlie_Test"),
            CreateScenarioInfo("Feature", "Alpha_Test"),
            CreateScenarioInfo("Feature", "Bravo_Test")
        };

        var features = infos.ToFeatures();

        Assert.AreEqual("Alpha test", features[0].Scenarios[0].DisplayName);
        Assert.AreEqual("Bravo test", features[0].Scenarios[1].DisplayName);
        Assert.AreEqual("Charlie test", features[0].Scenarios[2].DisplayName);
    }

    [TestMethod]
    public void ShouldReturnEmptyArrayWhenNoScenarioInfos()
    {
        var infos = Array.Empty<MSTestScenarioInfo>();

        var features = infos.ToFeatures();

        Assert.AreEqual(0, features.Length);
    }

    [TestMethod]
    public void ShouldHumanizePascalCaseScenarioDisplayNames()
    {
        var infos = new[] { CreateScenarioInfo("Feature", "ValidateOtpValidation") };

        var features = infos.ToFeatures();

        Assert.AreEqual("Validate otp validation", features[0].Scenarios[0].DisplayName);
    }

    [TestMethod]
    public void ShouldIncludeParametersFromTestDisplayName()
    {
        var infos = new[]
        {
            CreateScenarioInfo("Feature", "ValidateOtpValidation",
                testDisplayName: "ValidateOtpValidation (\"abc\",42)",
                testId: "Feature.ValidateOtpValidation1"),
            CreateScenarioInfo("Feature", "ValidateOtpValidation",
                testDisplayName: "ValidateOtpValidation (\"def\",99)",
                testId: "Feature.ValidateOtpValidation2")
        };

        var features = infos.ToFeatures();

        Assert.AreEqual(2, features[0].Scenarios.Length);
        Assert.AreEqual("Validate otp validation [\"abc\",42]", features[0].Scenarios[0].DisplayName);
        Assert.AreEqual("Validate otp validation [\"def\",99]", features[0].Scenarios[1].DisplayName);
    }

    [TestMethod]
    public void ShouldRebindPositionalParamsToNamedParamsWhenParameterNamesProvided()
    {
        var infos = new[]
        {
            CreateScenarioInfo("Feature", "ValidateOtp",
                testDisplayName: "ValidateOtp (\"abc\",42)",
                testId: "Feature.ValidateOtp1",
                parameterNames: new[] { "otp", "userId" }),
            CreateScenarioInfo("Feature", "ValidateOtp",
                testDisplayName: "ValidateOtp (\"def\",99)",
                testId: "Feature.ValidateOtp2",
                parameterNames: new[] { "otp", "userId" })
        };

        var features = infos.ToFeatures();

        var firstScenario = features[0].Scenarios[0];
        Assert.IsNotNull(firstScenario.ExampleValues);
        Assert.IsTrue(firstScenario.ExampleValues!.ContainsKey("otp"));
        Assert.IsTrue(firstScenario.ExampleValues!.ContainsKey("userId"));
    }

    [TestMethod]
    public void ShouldLeaveNamedParamsUnchangedWhenParameterNamesProvided()
    {
        var infos = new[]
        {
            CreateScenarioInfo("Feature", "TestMethod",
                testDisplayName: "TestMethod (otp: \"abc\", userId: 42)",
                testId: "Feature.TestMethod1",
                parameterNames: new[] { "otp", "userId" })
        };

        var features = infos.ToFeatures();

        var scenario = features[0].Scenarios[0];
        Assert.IsNotNull(scenario.ExampleValues);
        Assert.IsTrue(scenario.ExampleValues!.ContainsKey("otp"));
        Assert.IsTrue(scenario.ExampleValues!.ContainsKey("userId"));
    }

    [TestMethod]
    public void ShouldFallBackToStringParsingWhenParameterNamesNull()
    {
        var infos = new[]
        {
            CreateScenarioInfo("Feature", "TestMethod",
                testDisplayName: "TestMethod (\"abc\",42)",
                testId: "Feature.TestMethod1",
                parameterNames: null)
        };

        var features = infos.ToFeatures();

        var scenario = features[0].Scenarios[0];
        Assert.IsNotNull(scenario.ExampleValues);
        Assert.IsTrue(scenario.ExampleValues!.ContainsKey("arg0"));
    }

    [TestMethod]
    public void ShouldNotRebindWhenParameterCountDoesNotMatchParsedCount()
    {
        var infos = new[]
        {
            CreateScenarioInfo("Feature", "TestMethod",
                testDisplayName: "TestMethod (\"abc\",42)",
                testId: "Feature.TestMethod1",
                parameterNames: new[] { "onlyOne" })
        };

        var features = infos.ToFeatures();

        var scenario = features[0].Scenarios[0];
        Assert.IsNotNull(scenario.ExampleValues);
        // Should keep positional keys since count doesn't match
        Assert.IsTrue(scenario.ExampleValues!.ContainsKey("arg0"));
    }

    [TestMethod]
    public void ShouldPopulateStepsFromStepCollector()
    {
        var testId = "mstest-steps-" + Guid.NewGuid();

        StepCollector.StartStep(testId, "Given", "a valid request", null, null);
        StepCollector.CompleteStep(testId, true);
        StepCollector.StartStep(testId, "When", "the request is sent", null, null);
        StepCollector.CompleteStep(testId, true);

        var infos = new[] { CreateScenarioInfo(testId: testId) };

        try
        {
            var features = infos.ToFeatures();
            var scenario = features[0].Scenarios[0];

            Assert.IsNotNull(scenario.Steps);
            Assert.AreEqual(2, scenario.Steps!.Length);
            Assert.AreEqual("Given", scenario.Steps[0].Keyword);
            Assert.AreEqual("a valid request", scenario.Steps[0].Text);
            Assert.AreEqual("When", scenario.Steps[1].Keyword);
        }
        finally
        {
            StepCollector.ClearSteps(testId);
        }
    }

    [TestMethod]
    public void ShouldLeaveStepsNullWhenStepCollectorEmpty()
    {
        var infos = new[] { CreateScenarioInfo() };
        var features = infos.ToFeatures();
        Assert.IsNull(features[0].Scenarios[0].Steps);
    }

    [TestMethod]
    public void ShouldCarryWhenTheTestEnded()
    {
        var ended = new DateTimeOffset(2026, 1, 1, 10, 0, 3, 250, TimeSpan.Zero);
        var infos = new[]
        {
            new MSTestScenarioInfo
            {
                TestClassSimpleName = "FeatureA",
                TestMethodName = "Test1",
                TestId = "FeatureA.Test1",
                Outcome = UnitTestOutcome.Passed,
                EndedAt = ended
            }
        };

        var features = infos.ToFeatures();

        Assert.AreEqual(ended, features[0].Scenarios[0].EndedAt);
    }

    private static MSTestScenarioInfo CreateScenarioInfo(
        string className = "TestClass",
        string methodName = "TestMethod",
        string? testDisplayName = null,
        string? testId = null,
        UnitTestOutcome outcome = UnitTestOutcome.Passed,
        string? endpoint = null,
        bool isHappyPath = false,
        string? errorMessage = null,
        string? errorStackTrace = null,
        string[]? parameterNames = null)
    {
        return new MSTestScenarioInfo
        {
            TestClassSimpleName = className,
            TestMethodName = methodName,
            TestDisplayName = testDisplayName,
            TestId = testId ?? $"{className}.{methodName}",
            Outcome = outcome,
            Endpoint = endpoint,
            IsHappyPath = isHappyPath,
            ErrorMessage = errorMessage,
            ErrorStackTrace = errorStackTrace,
            ParameterNames = parameterNames
        };
    }
}
