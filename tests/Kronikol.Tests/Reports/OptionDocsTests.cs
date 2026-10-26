using System.Xml.Linq;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What an option's members say of themselves, read against the option (SEARCH_PLAN B14). An enum member's summary is
/// what an editor shows beside it, so one that calls itself the default has to be the value a fresh
/// <see cref="ReportConfigurationOptions"/> starts with: <see cref="NotePayloadFormat.Json"/> still called itself the
/// default years after 4.0.0 made <see cref="NotePayloadFormat.Yaml"/> the default.
/// </summary>
public class OptionDocsTests
{
    private static XDocument Docs()
    {
        var dll = typeof(ReportConfigurationOptions).Assembly.Location;
        return XDocument.Load(Path.ChangeExtension(dll, ".xml"));
    }

    // every enum member of the options whose summary calls it the default, with the option's own default
    public static TheoryData<string, string, string> MembersCalledTheDefault()
    {
        var docs = Docs();
        var summaries = docs.Descendants("member")
            .Where(m => ((string?)m.Attribute("name"))?.StartsWith("F:", StringComparison.Ordinal) == true)
            .ToDictionary(m => ((string)m.Attribute("name")!)[2..], m => m.Element("summary")?.Value ?? "");
        var options = new ReportConfigurationOptions();
        var data = new TheoryData<string, string, string>();
        foreach (var property in typeof(ReportConfigurationOptions).GetProperties().Where(p => p.PropertyType.IsEnum && p.CanRead))
        {
            var type = property.PropertyType;
            foreach (var name in Enum.GetNames(type))
            {
                if (!summaries.TryGetValue($"{type.FullName}.{name}", out var summary)) continue;
                if (!summary.Contains("(the default)", StringComparison.OrdinalIgnoreCase)) continue;
                data.Add(property.Name, name, property.GetValue(options)!.ToString()!);
            }
        }
        return data;
    }

    [Fact]
    public void NotePayloadFormat_calls_the_option_s_default_the_default()
    {
        var docs = Docs();
        string Summary(string member) => docs.Descendants("member")
            .Single(m => (string?)m.Attribute("name") == $"F:{typeof(NotePayloadFormat).FullName}.{member}")
            .Element("summary")!.Value;

        var @default = new ReportConfigurationOptions().NotePayloadFormat.ToString();
        Assert.Contains("(the default)", Summary(@default));
        foreach (var other in Enum.GetNames<NotePayloadFormat>().Where(n => n != @default))
            Assert.DoesNotContain("the default", Summary(other));
    }

    [Theory]
    [MemberData(nameof(MembersCalledTheDefault))]
    public void A_member_that_calls_itself_the_default_is_the_option_s_default(string option, string member, string optionDefault)
    {
        Assert.True(member == optionDefault, $"{option}: {member} calls itself the default, but the option starts at {optionDefault}");
    }
}
