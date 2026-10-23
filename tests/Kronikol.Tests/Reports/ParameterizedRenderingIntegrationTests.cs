using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Integration tests that simulate parameterized test data as produced by each framework adapter
/// (xUnit2, xUnit3, TUnit, NUnit4, MSTest, LightBDD, ReqNRoll, BDDfy), generate full HTML reports,
/// and verify that the rendered HTML uses correct R0/R1/R2/R3/R4 display rules —
/// covering scalars, complex records, truncated records, nested objects, and edge cases.
/// </summary>
public class ParameterizedRenderingIntegrationTests
{
    // ── Test data models ──

    private record AccountRiskScoreScenario(
        int AccountAgeInDays,
        int AccountRiskScore,
        int? ApplicationRiskScore,
        string ExpectedRiskband,
        string Reason);

    private record MerchantRequest(string Region, int Amount, string Currency);

    private record SimpleAddress(string Street, string City, string PostCode);

    private record CustomerProfile(string Name, int Age, string Email, SimpleAddress Address);

    private record LargeScenario(
        string A, string B, string C, string D, string E,
        string F, string G, string H, string I, string J, string K);

    private record NullableScenario(string Name, int? Score, string? Description, bool Active);

    private record CommaScenario(string Description, int Count);

    // ── Helpers ──

    private static Feature[] MakeFeature(params Scenario[] scenarios) =>
    [
        new Feature { DisplayName = "Integration Test Feature", Scenarios = scenarios }
    ];

    private static Scenario MakeScenario(
        string id, string displayName,
        ExecutionResult result = ExecutionResult.Passed,
        string? outlineId = null,
        Dictionary<string, string>? exampleValues = null,
        Dictionary<string, object?>? exampleRawValues = null,
        string? exampleDisplayName = null) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Result = result,
            OutlineId = outlineId,
            ExampleValues = exampleValues,
            ExampleRawValues = exampleRawValues,
            ExampleDisplayName = exampleDisplayName
        };

    private static string MakePlantUml(string scenarioName) =>
        $"@startuml\nActor -> Service: {scenarioName}\nService --> Actor: OK\n@enduml";

    private string GenerateReport(
        Feature[] features,
        bool groupParameterizedTests = true,
        int maxParameterColumns = 10,
        bool titleizeParameterNames = true)
    {
        var diagrams = features.SelectMany(f => f.Scenarios)
            .Select(s => new DefaultDiagramsFetcher.DiagramAsCode(s.Id, "", MakePlantUml(s.DisplayName)))
            .ToArray();
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"IntegrationTest_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: groupParameterizedTests,
            maxParameterColumns: maxParameterColumns,
            titleizeParameterNames: titleizeParameterNames);
        return ReportHtml.Read(path);
    }

    private static int CountOccurrences(string content, string substring)
    {
        var count = 0;
        var idx = 0;
        while ((idx = content.IndexOf(substring, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += substring.Length;
        }
        return count;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // xUnit2 adapter simulation: Parser-based, no ExampleRawValues
    // ═══════════════════════════════════════════════════════════════════════

    #region xUnit2 Adapter Patterns

    [Fact]
    public void XUnit2_InlineData_scalars_render_R1_columns()
    {
        // xUnit2 [Theory] + [InlineData(1, "UK", true)] → display name parsed
        var scenarios = new[]
        {
            MakeScenario("x2-1", "Authorise(amount: 100, region: UK, isActive: True)",
                outlineId: "Authorise",
                exampleValues: new() { ["amount"] = "100", ["region"] = "UK", ["isActive"] = "True" }),
            MakeScenario("x2-2", "Authorise(amount: 200, region: US, isActive: False)",
                outlineId: "Authorise",
                exampleValues: new() { ["amount"] = "200", ["region"] = "US", ["isActive"] = "False" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Amount</th>", content);
        Assert.Contains(">Region</th>", content);
        Assert.Contains(">Is Active</th>", content);
        Assert.Contains(">100</td>", content);
        Assert.Contains(">UK</td>", content);
    }

    [Fact]
    public void XUnit2_MemberData_record_flattens_R2_via_string_parsing()
    {
        // xUnit2 [MemberData] with record type → display name includes record ToString()
        // Note: C# record ToString() uses unquoted strings and empty for null
        var recordStr1 = "AccountRiskScoreScenario { AccountAgeInDays = 89, AccountRiskScore = 320, ApplicationRiskScore = null, ExpectedRiskband = \"E\", Reason = \"Pre 90. No application score present, account score in E band\" }";
        var recordStr2 = "AccountRiskScoreScenario { AccountAgeInDays = 120, AccountRiskScore = 500, ApplicationRiskScore = 400, ExpectedRiskband = \"A\", Reason = \"Post 90. Application score in A band\" }";
        var scenarios = new[]
        {
            MakeScenario("x2-r1", "Authorise(scenario)", outlineId: "Authorise",
                exampleValues: new() { ["scenario"] = recordStr1 }),
            MakeScenario("x2-r2", "Authorise(scenario)", outlineId: "Authorise",
                exampleValues: new() { ["scenario"] = recordStr2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Should flatten into individual columns (R2 string-based)
        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Account Age In Days</th>", content);
        Assert.Contains(">Account Risk Score</th>", content);
        Assert.Contains(">Application Risk Score</th>", content);
        Assert.Contains(">Expected Riskband</th>", content);
        Assert.Contains(">Reason</th>", content);
        Assert.Contains(">89</td>", content);
        Assert.Contains(">120</td>", content);
        Assert.Contains(">E</td>", content);
        Assert.Contains(">A</td>", content);
        // Must NOT show the raw record string
        Assert.DoesNotContain("AccountRiskScoreScenario {", content);
    }

    [Fact]
    public void XUnit2_ClassData_record_TRUNCATED_still_flattens_R2()
    {
        // THE BUG: xUnit2 truncates long display names → record ends with ··... instead of " }"
        var truncated1 = "AccountRiskScoreScenario { AccountAgeInDays = 89, AccountRiskScore = 320, ApplicationRiskScore = null, ExpectedRiskband = \"E\", Reason = \"Pre 90. No application score present, account scor\"\u00B7\u00B7...";
        var truncated2 = "AccountRiskScoreScenario { AccountAgeInDays = 120, AccountRiskScore = 500, ApplicationRiskScore = 400, ExpectedRiskband = \"A\", Reason = \"Post 90. Application score in A band, high confid\"\u00B7\u00B7...";
        var scenarios = new[]
        {
            MakeScenario("x2-t1", "Authorise(scenario)", outlineId: "Authorise",
                exampleValues: new() { ["scenario"] = truncated1 }),
            MakeScenario("x2-t2", "Authorise(scenario)", outlineId: "Authorise",
                exampleValues: new() { ["scenario"] = truncated2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Should still flatten into individual columns despite truncation
        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Account Age In Days</th>", content);
        Assert.Contains(">Account Risk Score</th>", content);
        Assert.Contains(">Expected Riskband</th>", content);
        Assert.Contains(">89</td>", content);
        Assert.Contains(">120</td>", content);
        Assert.Contains(">E</td>", content);
        Assert.Contains(">A</td>", content);
        // Must NOT show the raw truncated string
        Assert.DoesNotContain("AccountRiskScoreScenario {", content);
        Assert.DoesNotContain("\u00B7\u00B7...", content);
    }

    [Fact]
    public void XUnit2_truncated_record_mid_property_name_shows_completed_properties()
    {
        // Truncation cuts off mid-property — only fully parsed props should appear
        var truncated = "AccountRiskScoreScenario { AccountAgeInDays = 89, AccountRiskScore = 320, Applic\u00B7\u00B7...";
        var scenarios = new[]
        {
            MakeScenario("x2-tp1", "Test(scenario)", outlineId: "Test",
                exampleValues: new() { ["scenario"] = truncated }),
            MakeScenario("x2-tp2", "Test(scenario)", outlineId: "Test",
                exampleValues: new() { ["scenario"] = "AccountRiskScoreScenario { AccountAgeInDays = 120, AccountRiskScore = 500, Applic\u00B7\u00B7..." })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Should show at least the 2 fully parsed properties
        Assert.Contains(">Account Age In Days</th>", content);
        Assert.Contains(">Account Risk Score</th>", content);
        Assert.Contains(">89</td>", content);
        Assert.Contains(">320</td>", content);
    }

    [Fact]
    public void XUnit2_MemberData_with_nullable_properties_renders_correctly()
    {
        // Manually crafted record strings matching how they appear in xUnit display names
        var rec1 = "NullableScenario { Name = \"Alice\", Score = 95, Description = \"Top performer\", Active = True }";
        var rec2 = "NullableScenario { Name = \"Bob\", Score = null, Description = null, Active = False }";
        var scenarios = new[]
        {
            MakeScenario("x2-n1", "Evaluate(scenario)", outlineId: "Evaluate",
                exampleValues: new() { ["scenario"] = rec1 }),
            MakeScenario("x2-n2", "Evaluate(scenario)", outlineId: "Evaluate",
                exampleValues: new() { ["scenario"] = rec2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Name</th>", content);
        Assert.Contains(">Score</th>", content);
        Assert.Contains(">Description</th>", content);
        Assert.Contains(">Active</th>", content);
        Assert.Contains(">Alice</td>", content);
        Assert.Contains("><pre>null</pre></td>", content);
        Assert.Contains(">False</td>", content);
    }

    [Fact]
    public void XUnit2_record_with_commas_in_quoted_values_parses_correctly()
    {
        // Record strings with quoted values containing commas
        var rec1 = "CommaScenario { Description = \"Hello, world\", Count = 5 }";
        var rec2 = "CommaScenario { Description = \"Goodbye, cruel world\", Count = 10 }";
        var scenarios = new[]
        {
            MakeScenario("x2-c1", "Process(scenario)", outlineId: "Process",
                exampleValues: new() { ["scenario"] = rec1 }),
            MakeScenario("x2-c2", "Process(scenario)", outlineId: "Process",
                exampleValues: new() { ["scenario"] = rec2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Description</th>", content);
        Assert.Contains(">Count</th>", content);
        Assert.Contains(">Hello, world</td>", content);
        Assert.Contains(">5</td>", content);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════
    // xUnit3 adapter simulation: Structured extraction with ExampleRawValues
    // ═══════════════════════════════════════════════════════════════════════

    #region xUnit3 Adapter Patterns

    [Fact]
    public void XUnit3_InlineData_scalars_with_raw_values_render_R1()
    {
        var scenarios = new[]
        {
            MakeScenario("x3-1", "Authorise(amount: 100, region: UK)",
                outlineId: "Authorise",
                exampleValues: new() { ["amount"] = "100", ["region"] = "UK" },
                exampleRawValues: new() { ["amount"] = (object)100, ["region"] = "UK" }),
            MakeScenario("x3-2", "Authorise(amount: 200, region: US)",
                outlineId: "Authorise",
                exampleValues: new() { ["amount"] = "200", ["region"] = "US" },
                exampleRawValues: new() { ["amount"] = (object)200, ["region"] = "US" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Amount</th>", content);
        Assert.Contains(">Region</th>", content);
        Assert.Contains(">100</td>", content);
        Assert.Contains(">UK</td>", content);
    }

    [Fact]
    public void XUnit3_MemberData_record_with_raw_values_flattens_R2()
    {
        // xUnit3 has ExampleRawValues → reflection-based R2 flattening
        var obj1 = new MerchantRequest("UK", 100, "GBP");
        var obj2 = new MerchantRequest("US", 200, "USD");
        var scenarios = new[]
        {
            MakeScenario("x3-r1", "Process(request)", outlineId: "Process",
                exampleValues: new() { ["request"] = obj1.ToString()! },
                exampleRawValues: new() { ["request"] = obj1 }),
            MakeScenario("x3-r2", "Process(request)", outlineId: "Process",
                exampleValues: new() { ["request"] = obj2.ToString()! },
                exampleRawValues: new() { ["request"] = obj2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Region</th>", content);
        Assert.Contains(">Amount</th>", content);
        Assert.Contains(">Currency</th>", content);
        Assert.Contains(">UK</td>", content);
        Assert.Contains(">GBP</td>", content);
        Assert.DoesNotContain("MerchantRequest {", content);
    }

    [Fact]
    public void XUnit3_ClassData_mixed_scalar_and_complex_renders_R3_subtable()
    {
        // Multiple params: scalar "id" + complex "address" → R3 sub-table for address
        var addr1 = new SimpleAddress("1 Main St", "London", "SW1A");
        var addr2 = new SimpleAddress("2 Oak Ave", "Paris", "75001");
        var scenarios = new[]
        {
            MakeScenario("x3-m1", "Ship(id: 1, address: ...)", outlineId: "Ship",
                exampleValues: new() { ["id"] = "1", ["address"] = addr1.ToString()! },
                exampleRawValues: new() { ["id"] = (object)1, ["address"] = addr1 }),
            MakeScenario("x3-m2", "Ship(id: 2, address: ...)", outlineId: "Ship",
                exampleValues: new() { ["id"] = "2", ["address"] = addr2.ToString()! },
                exampleRawValues: new() { ["id"] = (object)2, ["address"] = addr2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // R3: sub-table for address column
        Assert.Contains("cell-subtable", content);
        Assert.Contains(">Street</th>", content);
        Assert.Contains(">City</th>", content);
        Assert.Contains("1 Main St", content);
        // Scalar column should still work
        Assert.Contains(">Id</th>", content);
    }

    [Fact]
    public void XUnit3_deeply_nested_object_renders_R4_expandable()
    {
        // Nested complex object → R4 expandable details
        var profile1 = new CustomerProfile("Alice", 30, "alice@test.com", new SimpleAddress("1 Main St", "London", "SW1A"));
        var profile2 = new CustomerProfile("Bob", 25, "bob@test.com", new SimpleAddress("2 Oak Ave", "Paris", "75001"));
        var scenarios = new[]
        {
            MakeScenario("x3-n1", "Enroll(id: A, profile: ...)", outlineId: "Enroll",
                exampleValues: new() { ["id"] = "A", ["profile"] = profile1.ToString()! },
                exampleRawValues: new() { ["id"] = (object)"A", ["profile"] = profile1 }),
            MakeScenario("x3-n2", "Enroll(id: B, profile: ...)", outlineId: "Enroll",
                exampleValues: new() { ["id"] = "B", ["profile"] = profile2.ToString()! },
                exampleRawValues: new() { ["id"] = (object)"B", ["profile"] = profile2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // R4: expandable details for the nested object
        Assert.Contains("param-expand", content);
        Assert.Contains("expand-body", content);
        Assert.Contains("prop-key", content);
        Assert.Contains("prop-val", content);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════
    // NUnit4 adapter simulation: Structured extraction with ExampleRawValues
    // ═══════════════════════════════════════════════════════════════════════

    #region NUnit4 Adapter Patterns

    [Fact]
    public void NUnit4_TestCase_scalars_render_R1()
    {
        // NUnit4 [TestCase(100, "UK")] → structured extraction from Test.Arguments
        var scenarios = new[]
        {
            MakeScenario("n4-1", "Validate(amount: 100, region: UK)",
                outlineId: "Validate",
                exampleValues: new() { ["amount"] = "100", ["region"] = "UK" },
                exampleRawValues: new() { ["amount"] = (object)100, ["region"] = "UK" }),
            MakeScenario("n4-2", "Validate(amount: 200, region: US)",
                outlineId: "Validate",
                exampleValues: new() { ["amount"] = "200", ["region"] = "US" },
                exampleRawValues: new() { ["amount"] = (object)200, ["region"] = "US" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Amount</th>", content);
        Assert.Contains(">Region</th>", content);
    }

    [Fact]
    public void NUnit4_TestCaseSource_record_flattens_R2_via_reflection()
    {
        var obj1 = new MerchantRequest("DE", 300, "EUR");
        var obj2 = new MerchantRequest("JP", 400, "JPY");
        var scenarios = new[]
        {
            MakeScenario("n4-r1", "Process(request)", outlineId: "Process",
                exampleValues: new() { ["request"] = obj1.ToString()! },
                exampleRawValues: new() { ["request"] = obj1 }),
            MakeScenario("n4-r2", "Process(request)", outlineId: "Process",
                exampleValues: new() { ["request"] = obj2.ToString()! },
                exampleRawValues: new() { ["request"] = obj2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Region</th>", content);
        Assert.Contains(">DE</td>", content);
        Assert.Contains(">EUR</td>", content);
        Assert.DoesNotContain("MerchantRequest {", content);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════
    // TUnit adapter simulation: Same as xUnit3/NUnit4 pattern
    // ═══════════════════════════════════════════════════════════════════════

    #region TUnit Adapter Patterns

    [Fact]
    public void TUnit_Arguments_scalars_render_R1()
    {
        var scenarios = new[]
        {
            MakeScenario("tu-1", "Submit(code: ABC, amount: 50)",
                outlineId: "Submit",
                exampleValues: new() { ["code"] = "ABC", ["amount"] = "50" },
                exampleRawValues: new() { ["code"] = (object)"ABC", ["amount"] = 50 }),
            MakeScenario("tu-2", "Submit(code: DEF, amount: 75)",
                outlineId: "Submit",
                exampleValues: new() { ["code"] = "DEF", ["amount"] = "75" },
                exampleRawValues: new() { ["code"] = (object)"DEF", ["amount"] = 75 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Code</th>", content);
        Assert.Contains(">Amount</th>", content);
        Assert.Contains(">ABC</td>", content);
    }

    [Fact]
    public void TUnit_record_with_raw_values_flattens_R2()
    {
        var obj1 = new NullableScenario("Test1", 90, "Desc 1", true);
        var obj2 = new NullableScenario("Test2", null, null, false);
        var scenarios = new[]
        {
            MakeScenario("tu-r1", "Run(scenario)", outlineId: "Run",
                exampleValues: new() { ["scenario"] = obj1.ToString()! },
                exampleRawValues: new() { ["scenario"] = obj1 }),
            MakeScenario("tu-r2", "Run(scenario)", outlineId: "Run",
                exampleValues: new() { ["scenario"] = obj2.ToString()! },
                exampleRawValues: new() { ["scenario"] = obj2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Name</th>", content);
        Assert.Contains(">Score</th>", content);
        Assert.Contains(">Test1</td>", content);
        Assert.Contains(">90</td>", content);
    }

    [Fact]
    public void TUnit_scalar_plus_small_complex_object_renders_R3_subtable()
    {
        // TUnit [Arguments] with scalar + small complex object → R3 sub-table for the complex param
        var addr1 = new SimpleAddress("10 High St", "Manchester", "M1 1AA");
        var addr2 = new SimpleAddress("20 Broad St", "Bristol", "BS1 2AB");
        var scenarios = new[]
        {
            MakeScenario("tu-r3a", "Ship(orderId: 101, address: ...)", outlineId: "Ship",
                exampleValues: new() { ["orderId"] = "101", ["address"] = addr1.ToString()! },
                exampleRawValues: new() { ["orderId"] = (object)101, ["address"] = addr1 }),
            MakeScenario("tu-r3b", "Ship(orderId: 102, address: ...)", outlineId: "Ship",
                exampleValues: new() { ["orderId"] = "102", ["address"] = addr2.ToString()! },
                exampleRawValues: new() { ["orderId"] = (object)102, ["address"] = addr2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // R1 scalar column for orderId
        Assert.Contains(">Order Id</th>", content);
        Assert.Contains(">101</td>", content);
        // R3 sub-table for address (≤5 scalar properties — uses raw property names, not titleized)
        Assert.Contains("cell-subtable", content);
        Assert.Contains(">Street</th>", content);
        Assert.Contains(">City</th>", content);
        Assert.Contains(">PostCode</th>", content);
        Assert.Contains("10 High St", content);
        Assert.Contains("Manchester", content);
    }

    [Fact]
    public void TUnit_scalar_plus_deeply_nested_object_renders_R4_expandable()
    {
        // TUnit [Arguments] with scalar + deeply nested object → R4 expandable for the complex param
        var profile1 = new CustomerProfile("Alice", 30, "alice@test.com", new SimpleAddress("1 Main St", "London", "SW1A"));
        var profile2 = new CustomerProfile("Bob", 25, "bob@test.com", new SimpleAddress("2 Oak Ave", "Paris", "75001"));
        var scenarios = new[]
        {
            MakeScenario("tu-r4a", "Enroll(tier: Gold, profile: ...)", outlineId: "Enroll",
                exampleValues: new() { ["tier"] = "Gold", ["profile"] = profile1.ToString()! },
                exampleRawValues: new() { ["tier"] = (object)"Gold", ["profile"] = profile1 }),
            MakeScenario("tu-r4b", "Enroll(tier: Silver, profile: ...)", outlineId: "Enroll",
                exampleValues: new() { ["tier"] = "Silver", ["profile"] = profile2.ToString()! },
                exampleRawValues: new() { ["tier"] = (object)"Silver", ["profile"] = profile2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // R1 scalar column for tier
        Assert.Contains(">Tier</th>", content);
        Assert.Contains(">Gold</td>", content);
        Assert.Contains(">Silver</td>", content);
        // R4 expandable for profile (has nested Address object)
        Assert.Contains("param-expand", content);
        Assert.Contains("expand-body", content);
        Assert.Contains("prop-key", content);
        Assert.Contains("prop-val", content);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════
    // MSTest adapter simulation: Parser-based, no ExampleRawValues
    // ═══════════════════════════════════════════════════════════════════════

    #region MSTest Adapter Patterns

    [Fact]
    public void MSTest_DataRow_scalars_render_R1()
    {
        // MSTest [DataRow(100, "UK")] → display name parsed, param names rebound
        var scenarios = new[]
        {
            MakeScenario("ms-1", "Authorise(amount: 100, region: UK)",
                outlineId: "Authorise",
                exampleValues: new() { ["amount"] = "100", ["region"] = "UK" }),
            MakeScenario("ms-2", "Authorise(amount: 200, region: US)",
                outlineId: "Authorise",
                exampleValues: new() { ["amount"] = "200", ["region"] = "US" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Amount</th>", content);
        Assert.Contains(">Region</th>", content);
    }

    [Fact]
    public void MSTest_DynamicData_record_flattens_R2_via_string_parsing()
    {
        // MSTest with [DynamicData] using record type → no raw values, string-based R2
        var obj1 = new MerchantRequest("AU", 150, "AUD");
        var obj2 = new MerchantRequest("CA", 250, "CAD");
        var scenarios = new[]
        {
            MakeScenario("ms-r1", "Validate(request)", outlineId: "Validate",
                exampleValues: new() { ["request"] = obj1.ToString()! }),
            MakeScenario("ms-r2", "Validate(request)", outlineId: "Validate",
                exampleValues: new() { ["request"] = obj2.ToString()! })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Region</th>", content);
        Assert.Contains(">Amount</th>", content);
        Assert.Contains(">Currency</th>", content);
        Assert.Contains(">AU</td>", content);
        Assert.Contains(">AUD</td>", content);
        Assert.DoesNotContain("MerchantRequest {", content);
    }

    [Fact]
    public void MSTest_DynamicData_TRUNCATED_record_still_flattens_R2()
    {
        // MSTest also truncates long display names
        var truncated1 = "MerchantRequest { Region = Australia and New Zealand combined territory, Amount = 150, Currency = AU\u00B7\u00B7...";
        var truncated2 = "MerchantRequest { Region = Canada northern provinces and territories combined, Amount = 250, Currency = CA\u00B7\u00B7...";
        var scenarios = new[]
        {
            MakeScenario("ms-t1", "Validate(request)", outlineId: "Validate",
                exampleValues: new() { ["request"] = truncated1 }),
            MakeScenario("ms-t2", "Validate(request)", outlineId: "Validate",
                exampleValues: new() { ["request"] = truncated2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Region</th>", content);
        Assert.Contains(">Amount</th>", content);
        Assert.Contains(">150</td>", content);
        Assert.Contains(">250</td>", content);
        Assert.DoesNotContain("MerchantRequest {", content);
        Assert.DoesNotContain("\u00B7\u00B7...", content);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════
    // LightBDD adapter simulation: Gherkin-style scenario outlines
    // ═══════════════════════════════════════════════════════════════════════

    #region LightBDD Adapter Patterns

    [Fact]
    public void LightBDD_scenario_outline_scalars_render_R1()
    {
        // LightBDD extracts params from step text into structured ExampleValues
        var scenarios = new[]
        {
            MakeScenario("lb-1", "Transaction with amount 100 in GB",
                outlineId: "Transaction with amount <amount> in <country>",
                exampleValues: new() { ["amount"] = "100", ["country"] = "GB" }),
            MakeScenario("lb-2", "Transaction with amount 200 in US",
                outlineId: "Transaction with amount <amount> in <country>",
                exampleValues: new() { ["amount"] = "200", ["country"] = "US" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Amount</th>", content);
        Assert.Contains(">Country</th>", content);
        Assert.Contains(">100</td>", content);
        Assert.Contains(">GB</td>", content);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════
    // ReqNRoll adapter simulation: Gherkin Examples: tables
    // ═══════════════════════════════════════════════════════════════════════

    #region ReqNRoll Adapter Patterns

    [Fact]
    public void ReqNRoll_scenario_outline_examples_render_R1()
    {
        // ReqNRoll pre-extracts ExampleValues from Gherkin Examples: tables
        var scenarios = new[]
        {
            MakeScenario("rn-1", "Purchase with card type Visa",
                outlineId: "Purchase with card type <cardType>",
                exampleValues: new() { ["cardType"] = "Visa", ["expectedResult"] = "Approved" }),
            MakeScenario("rn-2", "Purchase with card type MasterCard",
                outlineId: "Purchase with card type <cardType>",
                exampleValues: new() { ["cardType"] = "MasterCard", ["expectedResult"] = "Approved" }),
            MakeScenario("rn-3", "Purchase with card type Expired",
                outlineId: "Purchase with card type <cardType>",
                exampleValues: new() { ["cardType"] = "Expired", ["expectedResult"] = "Declined" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Card Type</th>", content);
        Assert.Contains(">Expected Result</th>", content);
        Assert.Contains(">Visa</td>", content);
        Assert.Contains(">Declined</td>", content);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════
    // BDDfy adapter simulation: Parser-based from scenario title
    // ═══════════════════════════════════════════════════════════════════════

    #region BDDfy Adapter Patterns

    [Fact]
    public void BDDfy_parameterized_scenario_title_render_R1()
    {
        // BDDfy bakes params into scenario title parsed by ParameterParser
        var scenarios = new[]
        {
            MakeScenario("bd-1", "Load account(accountId: ACC001, balance: 500)",
                outlineId: "Load account",
                exampleValues: new() { ["accountId"] = "ACC001", ["balance"] = "500" }),
            MakeScenario("bd-2", "Load account(accountId: ACC002, balance: 1000)",
                outlineId: "Load account",
                exampleValues: new() { ["accountId"] = "ACC002", ["balance"] = "1000" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Account Id</th>", content);
        Assert.Contains(">Balance</th>", content);
        Assert.Contains(">ACC001</td>", content);
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════
    // Cross-adapter edge cases
    // ═══════════════════════════════════════════════════════════════════════

    #region Edge Cases

    [Fact]
    public void Record_exceeding_maxColumns_falls_back_to_R4_expandable_in_cell()
    {
        // 11 properties → exceeds maxColumns=10 → R2 flattening rejected
        // Falls to R1 with single "scenario" column, cell renders as R4 expandable
        var rec = "LargeScenario { A = a, B = b, C = c, D = d, E = e, F = f, G = g, H = h, I = i, J = j, K = k }";
        var scenarios = new[]
        {
            MakeScenario("ec-1", "Test(scenario)", outlineId: "Test",
                exampleValues: new() { ["scenario"] = rec }),
            MakeScenario("ec-2", "Test(scenario)", outlineId: "Test",
                exampleValues: new() { ["scenario"] = rec })
        };
        var content = GenerateReport(MakeFeature(scenarios), maxParameterColumns: 10);

        // Should render as R1 with "Scenario" column, not R2 flattened columns
        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Scenario</th>", content);
        // Cell content should be R4 expandable (>5 props)
        Assert.Contains("param-expand", content);
        Assert.Contains("expand-body", content);
    }

    [Fact]
    public void Mixed_adapters_in_same_feature_render_correctly()
    {
        // Simulates a feature with both scalar and string-based record scenarios
        var obj = new MerchantRequest("UK", 100, "GBP");
        var scenarios = new[]
        {
            // Group 1: Simple scalars (xUnit style)
            MakeScenario("mix-1", "Calc(x: 1, y: 2)", outlineId: "Calc",
                exampleValues: new() { ["x"] = "1", ["y"] = "2" }),
            MakeScenario("mix-2", "Calc(x: 3, y: 4)", outlineId: "Calc",
                exampleValues: new() { ["x"] = "3", ["y"] = "4" }),
            // Group 2: Record flattening (record without raw values)
            MakeScenario("mix-3", "Process(request)", outlineId: "Process",
                exampleValues: new() { ["request"] = obj.ToString()! }),
            MakeScenario("mix-4", "Process(request)", outlineId: "Process",
                exampleValues: new() { ["request"] = new MerchantRequest("US", 200, "USD").ToString()! })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Both groups should render correctly
        Assert.True(CountOccurrences(content, "Input Parameters") >= 2);
        Assert.Contains(">X</th>", content);
        Assert.Contains(">Y</th>", content);
        Assert.Contains(">Region</th>", content);
        Assert.Contains(">Currency</th>", content);
    }

    [Fact]
    public void Truncated_record_with_plain_ellipsis_flattens_correctly()
    {
        // Some formatters use plain "..." instead of "··..."
        var truncated1 = "MerchantRequest { Region = UK, Amount = 100, Currency = GB...";
        var truncated2 = "MerchantRequest { Region = US, Amount = 200, Currency = US...";
        var scenarios = new[]
        {
            MakeScenario("pe-1", "Process(request)", outlineId: "Process",
                exampleValues: new() { ["request"] = truncated1 }),
            MakeScenario("pe-2", "Process(request)", outlineId: "Process",
                exampleValues: new() { ["request"] = truncated2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Region</th>", content);
        Assert.Contains(">Amount</th>", content);
        Assert.Contains(">UK</td>", content);
        Assert.Contains(">100</td>", content);
    }

    [Fact]
    public void R2_string_based_then_cell_level_R3_for_nested_record()
    {
        // Single-key record of a type that has a nested record property
        // R2 string-based flattening extracts properties, then the nested one renders as R3
        var scenarios = new[]
        {
            MakeScenario("nr-1", "Test(data)", outlineId: "Test",
                exampleValues: new() { ["data"] = "Order { Id = 1, Address = Place { Street = 1 Main St, City = London } }" }),
            MakeScenario("nr-2", "Test(data)", outlineId: "Test",
                exampleValues: new() { ["data"] = "Order { Id = 2, Address = Place { Street = 2 Oak Ave, City = Paris } }" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // R2 flattens into Id and Address columns
        Assert.Contains(">Id</th>", content);
        Assert.Contains(">Address</th>", content);
        Assert.Contains(">1</td>", content);
        // Nested record string "Place { ... }" should render as R3 sub-table in the Address cell
        Assert.Contains("cell-subtable", content);
        Assert.Contains("1 Main St", content);
    }

    [Fact]
    public void HTML_encodes_record_values_to_prevent_XSS()
    {
        var scenarios = new[]
        {
            MakeScenario("xss-1", "Test(data)", outlineId: "Test",
                exampleValues: new() { ["data"] = "Evil { Name = <script>alert(1)</script>, Count = 1 }" }),
            MakeScenario("xss-2", "Test(data)", outlineId: "Test",
                exampleValues: new() { ["data"] = "Evil { Name = safe, Count = 2 }" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.DoesNotContain("<script>alert(1)</script>", content);
        Assert.Contains("&lt;script&gt;", content);
    }

    [Fact]
    public void Scenario_with_ExampleDisplayName_always_falls_back_to_R0()
    {
        var scenarios = new[]
        {
            MakeScenario("edn-1", "Custom display name 1",
                outlineId: "Custom",
                exampleValues: new() { ["x"] = "1" },
                exampleDisplayName: "Custom display name 1"),
            MakeScenario("edn-2", "Custom display name 2",
                outlineId: "Custom",
                exampleValues: new() { ["x"] = "2" },
                exampleDisplayName: "Custom display name 2")
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("Test Case", content);
        Assert.DoesNotContain("Input Parameters", content);
    }

    [Fact]
    public void Real_world_AccountRiskScoreScenario_full_report_end_to_end()
    {
        // The exact scenario from the user's bug report
        var scenarios = new[]
        {
            MakeScenario("rw-1", "Authorise uses expected risk band(scenario)", outlineId: "Authorise uses expected risk band",
                exampleValues: new() { ["scenario"] = "AccountRiskScoreScenario { AccountAgeInDays = 89, AccountRiskScore = 320, ApplicationRiskScore = null, ExpectedRiskband = \"E\", Reason = \"Pre 90. No application score present, account scor\"\u00B7\u00B7..." }),
            MakeScenario("rw-2", "Authorise uses expected risk band(scenario)", outlineId: "Authorise uses expected risk band",
                exampleValues: new() { ["scenario"] = "AccountRiskScoreScenario { AccountAgeInDays = 120, AccountRiskScore = 500, ApplicationRiskScore = 400, ExpectedRiskband = \"A\", Reason = \"Post 90. Application score in A band\" }" }),
            MakeScenario("rw-3", "Authorise uses expected risk band(scenario)", outlineId: "Authorise uses expected risk band",
                exampleValues: new() { ["scenario"] = "AccountRiskScoreScenario { AccountAgeInDays = 45, AccountRiskScore = 150, ApplicationRiskScore = null, ExpectedRiskband = \"C\", Reason = \"Pre 90. No application score present, account score in C band\" }" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Must render as R2 flattened columns, NOT show raw record string
        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Account Age In Days</th>", content);
        Assert.Contains(">Account Risk Score</th>", content);
        Assert.Contains(">Application Risk Score</th>", content);
        Assert.Contains(">Expected Riskband</th>", content);
        Assert.Contains(">Reason</th>", content);

        // Values must appear in cells
        Assert.Contains(">89</td>", content);
        Assert.Contains(">120</td>", content);
        Assert.Contains(">45</td>", content);
        Assert.Contains(">E</td>", content);
        Assert.Contains(">A</td>", content);
        Assert.Contains(">C</td>", content);
        Assert.Contains("><pre>null</pre></td>", content);
        Assert.Contains(">400</td>", content);

        // Must NOT show the raw record string or truncation markers
        Assert.DoesNotContain("AccountRiskScoreScenario {", content);
        Assert.DoesNotContain("\u00B7\u00B7...", content);
    }

    [Fact]
    public void Truncated_and_non_truncated_records_in_same_group_both_flatten()
    {
        // Mix of truncated and non-truncated records in the same outline group
        var full = "Score { Value = 100, Band = A }";
        var truncated = "Score { Value = 200, Band = B\u00B7\u00B7...";
        var scenarios = new[]
        {
            MakeScenario("mt-1", "Assess(data)", outlineId: "Assess",
                exampleValues: new() { ["data"] = full }),
            MakeScenario("mt-2", "Assess(data)", outlineId: "Assess",
                exampleValues: new() { ["data"] = truncated })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Value</th>", content);
        Assert.Contains(">Band</th>", content);
        Assert.Contains(">100</td>", content);
        Assert.Contains(">A</td>", content);
        Assert.Contains(">200</td>", content);
    }

    [Fact]
    public void Multiple_truncated_records_with_different_truncation_points_flatten()
    {
        // Both rows truncated at different points
        var trunc1 = "Scenario { X = 1, Y = 2, Z = long value that gets trun\u00B7\u00B7...";
        var trunc2 = "Scenario { X = 3, Y = 4, Z = another long\u00B7\u00B7...";
        var scenarios = new[]
        {
            MakeScenario("dt-1", "Run(data)", outlineId: "Run",
                exampleValues: new() { ["data"] = trunc1 }),
            MakeScenario("dt-2", "Run(data)", outlineId: "Run",
                exampleValues: new() { ["data"] = trunc2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">X</th>", content);
        Assert.Contains(">Y</th>", content);
        Assert.Contains(">1</td>", content);
        Assert.Contains(">3</td>", content);
    }

    #endregion
}
