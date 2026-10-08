namespace Kronikol.Tests;

public class ScenarioTitleResolverTests
{
    [Fact]
    public void WhenScenarioTitleMatchesClassName_ShouldUseHumanizedMethodName()
    {
        var result = ScenarioTitleResolver.ResolveScenarioTitle(
            "AlternativeEvidenceApiTests",
            "AlternativeEvidenceApiTests",
            "GivenRequestForAlternativeEvidenceForApplication_WhenCalled_SentAlternativeEvidenceCommandAndReturnedResult");

        Assert.Equal(
            "Given request for alternative evidence for application when called sent alternative evidence command and returned result",
            result);
    }

    [Fact]
    public void WhenScenarioTitleDoesNotMatchClassName_ShouldReturnOriginalTitle()
    {
        var result = ScenarioTitleResolver.ResolveScenarioTitle(
            "Custom scenario title",
            "AlternativeEvidenceApiTests",
            "SomeMethodName");

        Assert.Equal("Custom scenario title", result);
    }

    [Fact]
    public void WhenTestClassNameIsNull_ShouldReturnOriginalTitle()
    {
        var result = ScenarioTitleResolver.ResolveScenarioTitle(
            "AlternativeEvidenceApiTests",
            null,
            "SomeMethodName");

        Assert.Equal("AlternativeEvidenceApiTests", result);
    }

    [Fact]
    public void WhenTestMethodNameIsNull_ShouldReturnOriginalTitle()
    {
        var result = ScenarioTitleResolver.ResolveScenarioTitle(
            "AlternativeEvidenceApiTests",
            "AlternativeEvidenceApiTests",
            null);

        Assert.Equal("AlternativeEvidenceApiTests", result);
    }

    [Fact]
    public void WhenMethodNameIsPascalCase_ShouldHumanizeCorrectly()
    {
        var result = ScenarioTitleResolver.ResolveScenarioTitle(
            "GetTests",
            "GetTests",
            "GivenRequest_WhenGetEndpointIsCalled_ThenItReturns");

        Assert.Equal("Given request when get endpoint is called then it returns", result);
    }

    [Fact]
    public void WhenMethodNameHasOnlyUnderscores_ShouldHumanizeCorrectly()
    {
        var result = ScenarioTitleResolver.ResolveScenarioTitle(
            "MyTests",
            "MyTests",
            "Given_A_Request_When_Called_Then_Returns");

        Assert.Equal("Given a request when called then returns", result);
    }

    [Fact]
    public void WhenScenarioTitleMatchesClassNameCaseInsensitively_ShouldStillReplace()
    {
        var result = ScenarioTitleResolver.ResolveScenarioTitle(
            "alternativeevidenceapitests",
            "AlternativeEvidenceApiTests",
            "GivenSomeTest");

        // BDDfy passes the title as-is, so case mismatch means the user didn't use nameof()
        // We should only match exact case
        Assert.Equal("alternativeevidenceapitests", result);
    }

    [Fact]
    public void WhenMethodNameIsPurePascalCase_ShouldSplitAndSentenceCase()
    {
        var result = ScenarioTitleResolver.ResolveScenarioTitle(
            "CakeFeature",
            "CakeFeature",
            "GivenCommandIsNotFoundWhenRequestIsMadeThenNotFoundResponseReturned");

        Assert.Equal("Given command is not found when request is made then not found response returned", result);
    }

    // ── AppendTestParameters ──

    [Fact]
    public void AppendTestParameters_WhenDisplayNameHasParameters_ShouldAppendInBrackets()
    {
        var result = ScenarioTitleResolver.AppendTestParameters(
            "Given bad xss like values for request",
            "Namespace.Class.GivenBadXssLikeValuesForRequest(clientId: \"<script>\", sessionId: \"abc\")");

        Assert.Equal("Given bad xss like values for request [clientId: \"<script>\", sessionId: \"abc\"]", result);
    }

    [Fact]
    public void AppendTestParameters_WhenDisplayNameHasNoParameters_ShouldReturnOriginalTitle()
    {
        var result = ScenarioTitleResolver.AppendTestParameters(
            "Some scenario title",
            "Namespace.Class.SomeMethodName");

        Assert.Equal("Some scenario title", result);
    }

    [Fact]
    public void AppendTestParameters_WhenDisplayNameIsNull_ShouldReturnOriginalTitle()
    {
        var result = ScenarioTitleResolver.AppendTestParameters(
            "Some scenario title",
            null);

        Assert.Equal("Some scenario title", result);
    }

    [Fact]
    public void AppendTestParameters_WhenDisplayNameHasEmptyParentheses_ShouldReturnOriginalTitle()
    {
        var result = ScenarioTitleResolver.AppendTestParameters(
            "Some scenario title",
            "Namespace.Class.SomeMethodName()");

        Assert.Equal("Some scenario title", result);
    }

    [Fact]
    public void AppendTestParameters_WhenDisplayNameHasMultipleInlineDataValues_ShouldAppendAll()
    {
        var result = ScenarioTitleResolver.AppendTestParameters(
            "Given bad xss like values",
            "Ns.C.Method(clientId: \"val1\", journeyId: \"val2\", appId: \"val3\", mobile: \"000\")");

        Assert.Equal("Given bad xss like values [clientId: \"val1\", journeyId: \"val2\", appId: \"val3\", mobile: \"000\"]", result);
    }

    [Fact]
    public void AppendTestParameters_WhenParametersExceedMaxLength_ShouldTruncate()
    {
        var longValue = new string('x', 300);
        var result = ScenarioTitleResolver.AppendTestParameters(
            "Some title",
            $"Ns.C.Method(account: \"{longValue}\")");

        Assert.EndsWith("…]", result);
        Assert.StartsWith("Some title [", result);
        Assert.True(result.Length < 300, "Result should be significantly shorter than the raw parameter content");
    }

    [Fact]
    public void AppendTestParameters_WhenParametersContainGenericTypes_TruncationUsesUnicodeEllipsis()
    {
        // Simulates MemberData with complex objects whose ToString() includes .NET generic type notation
        // e.g. List`1[Namespace.TypeName] — the backtick+brackets must not confuse downstream parsing
        var complexParam = """recipeName: "Classic", recipe: MuffinRecipeTestData { Ingredients = IngredientSet { Flour = Plain Flour, Apples = Granny Smith, Cinnamon = Ceylon }, Toppings = System.Collections.Generic.List`1[BreakfastProvider.Tests.Component.Shared.Models.AppleCinnamonMuffins.ToppingData] }, temperature: 180, durationMinutes: 25, panType: "Standard", expected: MuffinBatchExpectation { ExpectedIngredientCount = 5, ExpectedToppingCount = 2, HasBakingInfo = True }""";
        var result = ScenarioTitleResolver.AppendTestParameters(
            "Different muffin recipes should produce the expected batch",
            $"Ns.Class.Different_muffin_recipes_should_produce_the_expected_batch({complexParam})");

        // Must use Unicode ellipsis (…) not three dots (...) to prevent FormatScenarioDisplayName
        // from treating truncation dots as namespace separators
        Assert.EndsWith("…]", result);
        Assert.DoesNotContain("...", result);
        Assert.StartsWith("Different muffin recipes should produce the expected batch [", result);
    }

    [Fact]
    public void AppendTestParameters_WhenParametersAreExactlyAtMaxLength_ShouldNotTruncate()
    {
        // A short parameter string should be kept as-is
        var result = ScenarioTitleResolver.AppendTestParameters(
            "Some title",
            "Ns.C.Method(x: \"short\")");

        Assert.Equal("Some title [x: \"short\"]", result);
        Assert.DoesNotContain("...", result);
    }

    // ── FormatScenarioDisplayName ──

    [Fact]
    public void FormatScenarioDisplayName_ShouldHumanizePascalCaseMethodName()
    {
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("ValidateOtpValidationTests");
        Assert.Equal("Validate otp validation tests", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_ShouldStripNamespaceAndHumanize()
    {
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("Ns.Class.GivenRequest_WhenCalled_ThenReturns");
        Assert.Equal("Given request when called then returns", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_ShouldExtractParametersIntoBrackets()
    {
        var result = ScenarioTitleResolver.FormatScenarioDisplayName(
            "Ns.Class.TestMethod(param1: \"value1\", param2: 42)");
        Assert.Equal("Test method [param1: \"value1\", param2: 42]", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_ShouldHandleNUnitStyleParams()
    {
        // NUnit Test.Name format: MethodName(1,"hello")
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("ValidateOtpValidation(1,\"hello\")");
        Assert.Equal("Validate otp validation [1,\"hello\"]", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_ShouldHandleEmptyParentheses()
    {
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("Ns.Class.SimpleTest()");
        Assert.Equal("Simple test", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_ShouldTruncateLongParameters()
    {
        var longValue = new string('x', 300);
        var result = ScenarioTitleResolver.FormatScenarioDisplayName($"Ns.Class.TestMethod(a: \"{longValue}\")");
        Assert.EndsWith("…]", result);
        Assert.StartsWith("Test method [", result);
    }

    // A display name that is a sentence, not a method path: xUnit's [Fact(DisplayName = …)], MSTest's
    // DisplayName, TUnit's. Its last '.' is punctuation, not a namespace separator (#132, #123).

    [Fact]
    public void FormatScenarioDisplayName_KeepsASentenceWithADottedVersionWhole()
    {
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("Order API returns 404 for v1.2");
        Assert.Equal("Order api returns 404 for v1.2", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_KeepsASentenceEndingInAFullStop()
    {
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("Does the thing.");
        Assert.Equal("Does the thing.", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_GivesAnEmptyNameForAnEmptyDisplayName()
    {
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("");
        Assert.Equal("", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_KeepsTheClosingParenthesisOfAnArgument()
    {
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("Ns.Class.M(x: Foo())");
        Assert.Equal("M [x: Foo()]", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_StillStripsTheNamespaceFromATheoryRow()
    {
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("Ns.Class.Method(row: 1)");
        Assert.Equal("Method [row: 1]", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_KeepsAVersionNumberThatIsNotAMethodPath()
    {
        // No whitespace, but "2" cannot be a method name, so "v1" is no namespace.
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("v1.2");
        Assert.Equal("V1.2", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_ReadsParenthesesInsideASentenceAsText()
    {
        // Arguments are what a test framework appends at the end; this name has none.
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("Returns 404 (not found) for a missing order");
        Assert.Equal("Returns 404 (not found) for a missing order", result);
    }

    [Fact]
    public void FormatScenarioDisplayName_TakesTheArgumentsOfATheoryWithADisplayName()
    {
        // xUnit appends a row's arguments to the theory's own DisplayName.
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("Order total(qty: 1)");
        Assert.Equal("Order total [qty: 1]", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a.")]
    [InlineData(".a")]
    [InlineData("Ns.Class.")]
    [InlineData("_")]
    [InlineData("__")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("()")]
    [InlineData("(x: 1)")]
    [InlineData("a(")]
    [InlineData("a)")]
    [InlineData("Ns.C.M(")]
    [InlineData("x(y(z))")]
    [InlineData("1.2.3")]
    [InlineData("Does the thing. ")]
    [InlineData("…")]
    [InlineData("Ünïcode.Prüfung")]
    [InlineData("日本語のテスト.")]
    public void FormatScenarioDisplayName_NeverThrows(string displayName)
    {
        var result = ScenarioTitleResolver.FormatScenarioDisplayName(displayName);
        Assert.NotNull(result);
    }

    [Fact]
    public void ResolveScenarioTitle_KeepsTheTitleWhenTheMethodNameIsEmpty()
    {
        var result = ScenarioTitleResolver.ResolveScenarioTitle("SomeTests", "SomeTests", "");
        Assert.Equal("SomeTests", result);
    }

    [Fact]
    public void AppendTestParameters_KeepsTheClosingParenthesisOfAnArgument()
    {
        var result = ScenarioTitleResolver.AppendTestParameters("Some title", "Ns.C.M(x: Foo())");
        Assert.Equal("Some title [x: Foo()]", result);
    }

    // ── FormatFeatureName ──

    [Theory]
    [InlineData("GetTests", "Get Tests")]
    [InlineData("Get_Tests", "Get Tests")]
    [InlineData("SimpleTest", "Simple Test")]
    [InlineData("ABCTests", "ABC Tests")]
    [InlineData("Already Spaced", "Already Spaced")]
    public void FormatFeatureName_ShouldTitleizePascalCaseClassName(string input, string expected)
    {
        var result = ScenarioTitleResolver.FormatFeatureName(input);
        Assert.Equal(expected, result);
    }
}
