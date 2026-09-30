using System.Reflection;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// 4.0.0 removes the opt-in monospace note control (<c>plans/V4_PLAN.md</c> R8). 3.36.0 marked every public member
/// it took <see cref="ObsoleteAttribute"/> first (R7), as the roadmap requires of a major that removes API
/// (<c>ROADMAP.md</c> 11.1). The note width control, which shared its file, stays.
/// </summary>
public class MonospaceNoteControlRemovedTests
{
    public static TheoryData<string> Members() =>
    [
        $"{nameof(ReportConfigurationOptions)}.ShowNoteFontControls",
        $"{nameof(ReportToggleDefaults)}.NoteFont",
        $"{nameof(ResolvedToggleDefaults)}.NoteFont",
        $"{nameof(ResolvedToggleDefaults)}.ShowNoteFontControls",
    ];

    [Theory]
    [MemberData(nameof(Members))]
    public void Each_member_of_the_monospace_control_is_gone(string member)
    {
        var (typeName, propertyName) = (member[..member.IndexOf('.')], member[(member.IndexOf('.') + 1)..]);
        var type = typeof(ReportConfigurationOptions).Assembly.GetTypes().Single(t => t.Name == typeName && t.IsPublic);

        Assert.Null(type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
    }

    [Fact]
    public void The_font_enum_is_gone_and_the_width_control_stays()
    {
        var assembly = typeof(ReportConfigurationOptions).Assembly;

        Assert.Null(assembly.GetType("Kronikol.Reports.NoteFontFamily"));
        Assert.NotNull(typeof(ReportToggleDefaults).GetProperty(nameof(ReportToggleDefaults.NoteWidth)));
        Assert.NotNull(typeof(ResolvedToggleDefaults).GetProperty(nameof(ResolvedToggleDefaults.NoteWidth)));
    }
}
