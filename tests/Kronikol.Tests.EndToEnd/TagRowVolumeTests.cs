using System.Globalization;
using System.Text;
using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// How tall the Tags row paints with many tags (plans/TAGS_PLAN.md §4.8, Q9): reports with 0, 1, 9 (BreakfastProvider's
/// count), 40, 60, 100 and 400 distinct tags (400: a suite that tags every scenario with a ticket number), at 1280 and
/// 390 px, with the filter box and its rows open. A measurement, run by hand with <c>-- xUnit.Explicit=on</c>; it writes
/// <c>tag-row-volume.txt</c> to the run's output folder and fails only when the row is missing.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class TagRowVolumeTests : PlaywrightTestBase
{
    public TagRowVolumeTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly string[] BreakfastTags =
    [
        "IgnoreIfExternalSut", "SkipUnlessFakesControllable", "IgnoreIfNeedsDirectDbAccess", "IgnoreIfNeedsEventInfrastructure",
        "MenuDownstreamFailure", "MenuCaching", "MenuAvailability", "IgnoreUnlessInMemoryDb"
    ];

    private string Generate(int tags)
    {
        var scenarios = new List<Scenario>();
        var count = Math.Max(tags, 12);
        for (var i = 0; i < count; i++)
        {
            string[]? labels = tags switch
            {
                0 => null,
                1 => ["smoke"],
                // Happy Path and BreakfastProvider's eight
                9 => [BreakfastTags[i % BreakfastTags.Length]],
                _ => [string.Create(CultureInfo.InvariantCulture, $"PROJ-{1000 + i}")]
            };
            scenarios.Add(new Scenario
            {
                Id = $"v{i}", DisplayName = string.Create(CultureInfo.InvariantCulture, $"Scenario {i:000}"), Result = ExecutionResult.Passed,
                Duration = TimeSpan.FromMilliseconds(10), Tags = i < tags || tags == 9 ? labels : null, IsHappyPath = tags == 9 && i == 0
            });
        }
        var fileName = $"TagVolume_{tags}.html";
        var path = ReportGenerator.GenerateHtmlReport([], [new Feature { DisplayName = "Volume", Scenarios = [.. scenarios] }],
            DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Volume", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    [Fact(Explicit = true)]
    [Trait("Category", "Bench")]
    public async Task The_tags_row_s_painted_height_by_the_number_of_tags()
    {
        var lines = new StringBuilder();
        foreach (var (width, height) in new[] { (1280, 800), (390, 844) })
        {
            await Page.SetViewportSizeAsync(width, height);
            foreach (var tags in new[] { 0, 1, 9, 40, 60, 100, 400 })
            {
                await Page.GotoReportAsync(Generate(tags));
                await Page.Locator("details.feature").First.WaitForAsync();
                // a phone keeps the filter box behind its own Filters toggle
                var phoneToggle = Page.Locator(".mobile-filter-toggle");
                if (await phoneToggle.IsVisibleAsync())
                    await phoneToggle.ClickAsync();
                await OpenFilterRowsAsync();
                var row = await Page.EvaluateAsync<double[]>(
                    "() => { const r = document.querySelector('.tag-filters'); if (!r) return [0, 0]; const b = r.getBoundingClientRect(); return [b.height, r.querySelectorAll('.tag-toggle').length]; }");
                lines.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{width}x{height} tags={tags} chips={row[1]} rowHeight={row[0]:F0}px screens={row[0] / height:F2}"));
                if (tags > 0)
                    Assert.True(row[1] > 0, $"no Tags row at {width} px with {tags} tags");
            }
        }
        File.WriteAllText(Path.Combine(OutputDir, "tag-row-volume.txt"), lines.ToString());
    }
}
