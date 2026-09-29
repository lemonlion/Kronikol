using System.Reflection;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// 4.0.0 removes the opt-in monospace note control (<c>plans/V4_PLAN.md</c> R8), and the roadmap allows no major that
/// removes API without a release that deprecated it first (<c>ROADMAP.md</c> 11.1). R7 is that release: every public
/// member the removal takes is <see cref="ObsoleteAttribute"/>, with a message that says what goes and what stays.
/// </summary>
public class MonospaceNoteControlDeprecationTests
{
    public static TheoryData<string> Members() =>
    [
        $"{nameof(ReportConfigurationOptions)}.ShowNoteFontControls",
        $"{nameof(ReportToggleDefaults)}.NoteFont",
        $"{nameof(ResolvedToggleDefaults)}.NoteFont",
        $"{nameof(ResolvedToggleDefaults)}.ShowNoteFontControls",
        "NoteFontFamily",
    ];

    [Theory]
    [MemberData(nameof(Members))]
    public void Each_member_the_4_0_0_removal_takes_is_obsolete_and_says_so(string member)
    {
        MemberInfo target = member switch
        {
            "NoteFontFamily" => typeof(ReportConfigurationOptions).Assembly.GetType("Kronikol.Reports.NoteFontFamily")!,
            _ => Property(member),
        };

        var obsolete = target.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete.IsError, "a warning in 3.x: code that sets it still builds until 4.0.0");
        Assert.Contains("4.0.0", obsolete.Message, StringComparison.Ordinal);
        Assert.Contains("note width", obsolete.Message, StringComparison.Ordinal);
    }

    private static PropertyInfo Property(string member)
    {
        var (typeName, propertyName) = (member[..member.IndexOf('.')], member[(member.IndexOf('.') + 1)..]);
        var type = typeof(ReportConfigurationOptions).Assembly.GetTypes().Single(t => t.Name == typeName && t.IsPublic);
        return type.GetProperty(propertyName)!;
    }
}
