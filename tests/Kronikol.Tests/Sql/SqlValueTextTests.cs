using Kronikol.Sql;

namespace Kronikol.Tests.Sql;

/// <summary>
/// A single value a SQL capture writes as text, a scalar result or a parameter, reads as the same value does in a row
/// (which is written as JSON): the same on every machine. Until 4.16.3 each wrapper wrote it with the machine's
/// culture, so <c>SELECT 2.5</c> answered <c>2,5</c> on a German machine and a date came out in the local calendar,
/// beside rows of the same report written <c>2.5</c> and ISO 8601.
/// </summary>
public class SqlValueTextTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    [InlineData("sv-SE")]
    public void A_value_reads_as_it_does_in_a_row_on_every_machine(string culture)
    {
        var texts = CultureRun.Under(culture, () => new[]
        {
            SqlRowFormatter.ValueText(2.5),
            SqlRowFormatter.ValueText(12.5m),
            SqlRowFormatter.ValueText(-1),
            SqlRowFormatter.ValueText(new DateTime(2026, 10, 7, 11, 50, 57)),
            SqlRowFormatter.ValueText(new DateTimeOffset(2026, 10, 7, 11, 50, 57, TimeSpan.FromHours(2))),
            SqlRowFormatter.ValueText(new DateOnly(2026, 10, 7)),
            SqlRowFormatter.ValueText(TimeSpan.FromSeconds(90)),
            SqlRowFormatter.ValueText("räksmörgås 2,5"),
            SqlRowFormatter.ValueText(new byte[] { 1, 2, 3 }),
            SqlRowFormatter.ValueText(true)
        });

        Assert.Equal(
        [
            "2.5", "12.5", "-1", "2026-10-07T11:50:57", "2026-10-07T11:50:57+02:00", "2026-10-07", "00:01:30",
            "räksmörgås 2,5", "[bytes: 3]", "True"
        ], texts);
    }
}
