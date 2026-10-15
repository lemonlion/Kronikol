namespace Kronikol.Tests.Ingestion.Cucumber;

/// <summary>
/// The checked-in run under <c>TestData/Cucumber/playwright-bdd-9.2-attachments/</c> (#175): a real playwright-bdd
/// 9.2.0 run on @playwright/test 1.62.1 in Chromium, with <c>retries: 1</c>, Playwright's own artefacts on failure
/// (<c>screenshot</c>, <c>video</c>, <c>trace</c>, <c>error-context</c>), the wiki's reporter recipe writing the tests
/// file, the wiki's start/end screenshot fixture and its <c>AfterStep</c> screenshot on every Gherkin step. Both of the
/// run's descriptions of every artefact are here: the tests file names the file Playwright or the reporter wrote, and
/// the Cucumber messages embed the same bytes.
/// </summary>
/// <remarks>
/// Made by <c>plans/ATTACHMENT_MERGE_PLAN.harness/probe/</c> (its README has the commands) with
/// <c>PROBE_SMALL=1</c>, a 48x27 page and a trace without screenshots, snapshots or sources, so the whole run is about
/// 340 KB, and cut by the harness's <c>cut_fixture.py</c>: the tests file names its files relative to this folder
/// (<c>AttachmentsBase</c>), and this machine's paths became <c>C:\fixture</c> in the messages, the tests file and
/// inside each <c>trace.zip</c>, whose base64 copy in the messages was re-encoded from the same new bytes. Nothing in the
/// folder is converted on checkout (<c>.gitattributes</c>): the facts compare its files with the messages' copies byte
/// for byte.
/// </remarks>
internal static class AttachmentFixture
{
    public static string Directory => Path.Combine(CucumberFixtures.Directory, "playwright-bdd-9.2-attachments");

    /// <summary>The run's Cucumber messages (playwright-bdd's <c>cucumberReporter('message')</c>).</summary>
    public static string MessagesPath => Path.Combine(Directory, "messages.ndjson");

    /// <summary>The run's tests file, as the wiki's reporter recipe writes it.</summary>
    public static string TestsPath => Path.Combine(Directory, "tests.ndjson");

    /// <summary>
    /// Fails on both attempts. Its last attempt: <c>screenshot-start.png</c> and <c>screenshot-end.png</c> from the
    /// fixture, <c>after-step.png</c> on each of its three steps, <c>step-shot.png</c> on its second, and Playwright's
    /// <c>screenshot</c>, <c>video</c>, <c>error-context</c> and <c>trace</c>: ten artefacts.
    /// </summary>
    public const string FailingScenario = "A failing UI scenario keeps Playwright's artefacts";

    /// <summary>Passes. Its second step attaches two <c>note</c> JSON bodies, which the messages carry as text.</summary>
    public const string PassingScenario = "A passing scenario attaches the same name twice";

    /// <summary>Fails its first attempt and passes its second, which makes four artefacts and no built-in.</summary>
    public const string FlakyScenario = "A flaky scenario passes on its retry";

    /// <summary>Its last step attaches <c>kronikol-bypass</c>, which the reporter recipe writes as a file.</summary>
    public const string BypassScenario = "A step attaches kronikol-bypass";

    /// <summary>The failing scenario's shape under a <c>Background:</c> step, whose screenshot is pinned to it.</summary>
    public const string BackgroundScenario = "A failing scenario under a background";

    /// <summary>The failing scenario's steps, in order.</summary>
    public static readonly string[] FailingSteps = ["the page is open", "a step attaches a named image", "the check fails"];
}
