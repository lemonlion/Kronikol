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
        // and its word in capitals: 5.16.4 read "Order api returns 404 for v1.2" (#139, F1)
        var result = ScenarioTitleResolver.FormatScenarioDisplayName("Order API returns 404 for v1.2");
        Assert.Equal("Order API returns 404 for v1.2", result);
    }

    [Theory]
    [InlineData("WhenMCAFundingActivates_TheLOCIsDrawn", "When MCA funding activates the LOC is drawn")]
    [InlineData("Ns.OrderTests.Returns404WhenMissing(id: 7)", "Returns404 when missing [id: 7]")]
    [InlineData("Ns.OrderTests.ShipsToTheUK(country: \"GB\")", "Ships to the UK [country: \"GB\"]")]
    public void FormatScenarioDisplayName_KeepsTheWordsAMethodWritesInCapitals(string displayName, string expected) =>
        Assert.Equal(expected, ScenarioTitleResolver.FormatScenarioDisplayName(displayName));

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

    // ── xUnit ──

    /// <summary>The defaults the xUnit lanes pass: every methodDisplay and methodDisplayOptions xUnit has.</summary>
    private static (string Name, string Method)[] XunitDefaults(string className, string methodName) =>
        Enumerable.Range(0, 16).Select(o => (Xunit.Sdk.TestMethodDisplayOptions)o).SelectMany(options =>
        {
            var method = new Xunit.v3.DisplayNameFormatter(Xunit.Sdk.TestMethodDisplay.Method, options).Format(methodName);
            return new[]
            {
                (new Xunit.v3.DisplayNameFormatter(Xunit.Sdk.TestMethodDisplay.ClassAndMethod, options).Format($"{className}.{methodName}"), method),
                (method, method)
            };
        }).ToArray();

    // xUnit's default display name, which methodDisplayOptions format whole: the namespace is removed whatever its
    // spaces (4.11.1 kept it, since a name with whitespace read as a sentence) and whatever its dots became.
    [Theory]
    [InlineData("Ns.OrderTests.Returns_404_for_missing_order", "Ns.OrderTests", "Returns_404_for_missing_order", "Returns 404 for missing order")]
    [InlineData("Ns.OrderTests.Returns 404 for missing order", "Ns.OrderTests", "Returns_404_for_missing_order", "Returns 404 for missing order")]
    [InlineData("My Ns.Order Tests.Returns 404 for missing order", "My_Ns.Order_Tests", "Returns_404_for_missing_order", "Returns 404 for missing order")]
    [InlineData("Ns, OrderTests, Returns 404 for missing order", "Ns.OrderTests", "Returns_404_for_missing_order", "Returns 404 for missing order")]
    [InlineData("Ns.OrderTests.Places(qty: 1, sku: \"a.b\")", "Ns.OrderTests", "Places", "Places [qty: 1, sku: \"a.b\"]")]
    [InlineData("Ns.OrderTests.Places a order(qty: 1)", "Ns.OrderTests", "Places_a_order", "Places a order [qty: 1]")]
    [InlineData("Ns.OrderTests.Places<Int32, String>(a: 1, b: 2)", "Ns.OrderTests", "Places", "Places<int32, string> [a: 1, b: 2]")]
    [InlineData("Ns.OrderTests.Places<List<Int32>>(a: 1)", "Ns.OrderTests", "Places", "Places<list<int32>> [a: 1]")]
    [InlineData("Returns_404", "Ns.OrderTests", "Returns_404", "Returns 404")]
    [InlineData("Returns 404(id: Foo())", "Ns.OrderTests", "Returns_404", "Returns 404 [id: Foo()]")]
    [InlineData("Ns.OrderTests.ShipsToTheUK(country: \"GB\")", "Ns.OrderTests", "ShipsToTheUK", "Ships to the UK [country: \"GB\"]")]
    [InlineData("Ns.OrderTests.Total >= 100", "Ns.OrderTests", "Total_ge_100", "Total >= 100")]
    // useEscapeSequences decodes a dot into the method's own name: 5.16.4 cut there and read "2"
    [InlineData("Ns.OrderTests.Supports_v1.2", "Ns.OrderTests", "Supports_v1X2E2", "Supports v1.2")]
    // a row's label is what follows a generated name in brackets (B1: 5.16.4 cut it at its dot and lower-cased it)
    [InlineData("Ns.OrderTests.Ships [U.K.]", "Ns.OrderTests", "Ships", "Ships [U.K.]")]
    public void FormatXunitScenarioName_ReadsXunitsDefaultNameByTheRule(string displayName, string className, string methodName, string expected)
    {
        var name = ScenarioTitleResolver.FormatXunitScenarioName(displayName, null, XunitDefaults(className, methodName), "qty");
        Assert.Equal(expected, name.Shown);
        Assert.False(name.Written);
        Assert.True(name.ReadArguments);
    }

    // A DisplayName the test set is shown as written, with its own dots, parentheses and capitals; a theory row's
    // arguments are what xUnit appended after it, and a label what it appended in brackets.
    [Theory]
    [InlineData("Returns 404 (not found)", "Returns 404 (not found)", "Returns 404 (not found)", false)]
    [InlineData("Returns 404 (not found)(id: 1)", "Returns 404 (not found)", "Returns 404 (not found) [id: 1]", true)]
    [InlineData("System.Text.Json parses numbers", "System.Text.Json parses numbers", "System.Text.Json parses numbers", false)]
    [InlineData("Order API returns 404 for v1.2", "Order API returns 404 for v1.2", "Order API returns 404 for v1.2", false)]
    [InlineData("Orders are sent to Kafka and stored in MongoDB", "Orders are sent to Kafka and stored in MongoDB", "Orders are sent to Kafka and stored in MongoDB", false)]
    [InlineData("iOS users see the new checkout", "iOS users see the new checkout", "iOS users see the new checkout", false)]
    [InlineData("Does the thing.", "Does the thing.", "Does the thing.", false)]
    // B1: 5.16.4 read the label as arguments of its own, "Ships to the uk [ [UK]]"
    [InlineData("Ships to the UK [UK]", "Ships to the UK", "Ships to the UK [UK]", true)]
    public void FormatXunitScenarioName_ShowsADisplayNameTheTestSetAsWritten(string displayName, string explicitDisplayName, string expected, bool readArguments)
    {
        var name = ScenarioTitleResolver.FormatXunitScenarioName(displayName, explicitDisplayName, XunitDefaults("Ns.OrderTests", "Ships"), "id");
        Assert.Equal(expected, name.Shown);
        Assert.True(name.Written);
        Assert.Equal(readArguments, name.ReadArguments);
    }

    // xUnit v3 names a row by its own TestDisplayName where it has one, in place of the base: a name the test wrote.
    // 5.16.4 read it as a method path and kept what followed its last dot (B1).
    [Theory]
    [InlineData("Order v1.2 ships(qty: 1)", "Order v1.2 ships [qty: 1]")]
    [InlineData("Returns 404 (not found)(qty: 2)", "Returns 404 (not found) [qty: 2]")]
    [InlineData("Ships to the U.K. [UK row]", "Ships to the U.K. [UK row]")]
    public void FormatXunitScenarioName_ShowsARowsOwnNameAsWritten(string displayName, string expected)
    {
        var name = ScenarioTitleResolver.FormatXunitScenarioName(displayName, null, XunitDefaults("Ns.OrderTests", "Ships"), "qty");
        Assert.Equal(expected, name.Shown);
        Assert.True(name.Written);
        Assert.True(name.ReadArguments);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("(", null)]
    [InlineData(".", null)]
    [InlineData(", ", null)]
    [InlineData("<", null)]
    [InlineData("x", "")]
    [InlineData("x", "y")]
    [InlineData("x(", "x")]
    [InlineData("x)", null)]
    [InlineData("Ns.C.M<", null)]
    public void FormatXunitScenarioName_NeverThrows(string displayName, string? explicitDisplayName) =>
        Assert.NotNull(ScenarioTitleResolver.FormatXunitScenarioName(displayName, explicitDisplayName, XunitDefaults("Ns.C", "M"), "x").Shown);

    // ── TUnit, NUnit, MSTest ──

    // The lanes' default name is the method's, with its arguments after it: read by the rule.
    [Theory]
    [InlineData("Returns404WhenMissing", "Returns404WhenMissing", "Returns404 when missing")]
    [InlineData("Returns404WhenMissing(7)", "Returns404WhenMissing", "Returns404 when missing [7]")]
    [InlineData("Adds (1,2)", "Adds", "Adds [1,2]")]
    [InlineData("Places<Int32>(1)", "Places", "Places<int32> [1]")]
    [InlineData("Ns.OrderTests.ShipsToTheUK(\"GB\")", "ShipsToTheUK", "Ships to the UK [\"GB\"]")]
    [InlineData("OrderTests.ShipsToTheUK", "ShipsToTheUK", "Ships to the UK")]
    public void FormatLaneScenarioName_ReadsTheMethodsDefaultNameByTheRule(string name, string methodName, string expected)
    {
        var read = ScenarioTitleResolver.FormatLaneScenarioName(name, methodName);
        Assert.Equal(expected, read.Shown);
        Assert.False(read.Written);
    }

    // Any other name is one the test wrote (a [DisplayName], a TestName, a DisplayName): shown as written, not split,
    // re-cased, cut at a dot or re-bracketed (F2, B2). The boundary after the method's name matters both ways.
    [Theory]
    [InlineData("Orders are sent to Kafka and stored in MongoDB", "Order")]
    [InlineData("Login via PayPal", "Login")]
    [InlineData("AcmePay rejects a webhook without a signature", "Rejects")]
    [InlineData("Returns 404 (not found)", "Returns404")]
    [InlineData("Checkout.Works", "Checkout_works")]
    [InlineData("When MCA funding activates, the LOC is drawn", "Draws")]
    public void FormatLaneScenarioName_ShowsANameTheTestWroteAsWritten(string name, string methodName)
    {
        var read = ScenarioTitleResolver.FormatLaneScenarioName(name, methodName);
        Assert.Equal(name, read.Shown);
        Assert.True(read.Written);
        Assert.False(read.ReadArguments);
    }

    // MSTest names a data row under a [TestMethod(DisplayName = ...)] by that DisplayName and the row's arguments.
    [Theory]
    [InlineData("Adds VAT (1,2)", "Adds VAT [1,2]", true)]
    [InlineData("Adds VAT(1,2)", "Adds VAT [1,2]", true)]
    [InlineData("Adds VAT", "Adds VAT", false)]
    public void FormatLaneScenarioName_ShowsAWrittenBaseWithItsRowsArguments(string name, string expected, bool readArguments)
    {
        var read = ScenarioTitleResolver.FormatLaneScenarioName(name, "Adds", writtenBase: "Adds VAT");
        Assert.Equal(expected, read.Shown);
        Assert.True(read.Written);
        Assert.Equal(readArguments, read.ReadArguments);
    }

    [Fact]
    public void FormatLaneScenarioName_NamesAnUnnamedTestByItsMethod()
    {
        var read = ScenarioTitleResolver.FormatLaneScenarioName(" ", "GetHTTPResponse");
        Assert.Equal("Get HTTP response", read.Shown);
        Assert.False(read.Written);
    }

    // ── 5.16.4's rule, frozen for the ids ──

    /// <summary>The fixture's rows: a test method's name, the title 5.12.0 to 5.16.4 gave it, the title the rule gives it.</summary>
    public static TheoryData<string, string, string> TitlesByRelease()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "titles-by-release.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var fields = line.Split('\t');
            data.Add(fields[0], fields[1], fields[2]);
        }
        return data;
    }

    [Fact]
    public void The_fixture_holds_every_title_the_rule_changes()
    {
        var rows = TitlesByRelease().ToArray();
        Assert.Equal(1437, rows.Length);
        Assert.Equal(437, rows.Count(r => r.Data.Item2 != r.Data.Item3));
    }

    // The frozen copies give 5.16.4's titles for ever: an edit to one would move the id of every scenario it touched.
    // The rule gives its own, measured by the plan's harness over Kronikol's own tests.
    [Theory]
    [MemberData(nameof(TitlesByRelease))]
    public void A_corpus_name_keeps_its_frozen_title_and_reads_by_the_rule(string method, string frozenTitle, string title)
    {
        Assert.Equal(frozenTitle, ScenarioTitleResolver.FormatScenarioDisplayNameAsOf5164(method));
        Assert.Equal(frozenTitle, ScenarioTitleResolver.FormatXunitDisplayNameAsOf5164($"Ns.Tests.{method}", null));
        Assert.Equal(frozenTitle, ScenarioTitleResolver.ResolveScenarioTitleAsOf5164("Tests", "Tests", method));
        Assert.Equal(title, ScenarioTitleResolver.FormatScenarioDisplayName(method));
    }

    // 5.16.4's xUnit readings, kept by the frozen copy whatever the shown name now is.
    [Theory]
    [InlineData("Ns.OrderTests.Places<Int32, String>(a: 1, b: 2)", null, "Places<int32, string> [a: 1, b: 2]")]
    [InlineData("System.Text.Json parses numbers", "System.Text.Json parses numbers", "System.text.json parses numbers")]
    [InlineData("Order API returns 404 for v1.2", "Order API returns 404 for v1.2", "Order api returns 404 for v1.2")]
    [InlineData("Ships to the UK [UK]", "Ships to the UK", "Ships to the uk [ [UK]]")]
    [InlineData("Order v1.2 ships(qty: 1)", null, "2 ships [qty: 1]")]
    [InlineData("Ns.OrderTests.Supports_v1.2", null, "2")]
    public void FormatXunitDisplayNameAsOf5164_KeepsWhat5161Read(string displayName, string? explicitDisplayName, string expected) =>
        Assert.Equal(expected, ScenarioTitleResolver.FormatXunitDisplayNameAsOf5164(displayName, explicitDisplayName));

    [Theory]
    [InlineData("Returns 404 (not found)", "Returns 404 [not found]")]
    [InlineData("Order.Ships", "Ships")]
    [InlineData("AcmePay rejects a webhook", "Acme pay rejects a webhook")]
    public void FormatScenarioDisplayNameAsOf5164_KeepsWhat5161Read(string displayName, string expected) =>
        Assert.Equal(expected, ScenarioTitleResolver.FormatScenarioDisplayNameAsOf5164(displayName));

    [Fact]
    public void AppendTestParameters_ReadsNoArgumentsFromAParenthesisInMidSentence() =>
        Assert.Equal("Returns a 404", ScenarioTitleResolver.AppendTestParameters("Returns a 404", "Returns 404 (not found) for a missing order"));

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
    [Fact]
    public void A_title_reads_the_same_on_a_machine_whose_culture_cases_i_otherwise()
    {
        // Turkish and Azeri give 'i' a dotted capital and 'I' a dotless small letter. Cased with the machine's culture,
        // "it_rejects_an_empty_order" read "İt rejects…" there, and its stableId differed from every other machine's, so
        // history, baselines and merges did not pair the runs (until 4.16.3).
        var (title, feature, camel) = CultureRun.Under("tr-TR", () => (
            ScenarioTitleResolver.ResolveScenarioTitle("OrderTests", "OrderTests", "it_rejects_an_empty_order"),
            ScenarioTitleResolver.FormatFeatureName("order_items_tests"),
            "Inventory".Camelize()));

        Assert.Equal("It rejects an empty order", title);
        Assert.Equal("Order Items Tests", feature);
        Assert.Equal("inventory", camel);
    }

    [Fact]
    public void A_frozen_title_reads_the_same_on_a_machine_whose_culture_cases_i_otherwise()
    {
        var (frozen, xunit) = CultureRun.Under("az-Latn-AZ", () => (
            ScenarioTitleResolver.FormatScenarioDisplayNameAsOf5164("it_rejects_an_IBAN"),
            ScenarioTitleResolver.FormatXunitDisplayNameAsOf5164("Ns.C.it_rejects_an_IBAN", null)));

        Assert.Equal("It rejects an iban", frozen);
        Assert.Equal("It rejects an iban", xunit);
    }
}
