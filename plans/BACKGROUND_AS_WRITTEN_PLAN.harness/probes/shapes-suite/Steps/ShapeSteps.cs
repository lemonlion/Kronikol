using Kronikol.Tracking;
using Reqnroll;

namespace Shapes.Suite.Steps;

/// <summary>Every step definition calls a woven sub-step naming the scenario that really ran it, so a step in the report says whose it is.</summary>
[Binding]
public class ShapeSteps(ShapeDriver driver, ScenarioContext scenario, ITestRunner runner, IReqnrollOutputHelper output)
{
    private string Who => scenario.ScenarioInfo.Title + RowOf();

    private string RowOf()
    {
        var arguments = scenario.ScenarioInfo.Arguments;
        return arguments is { Count: > 0 } ? " #" + string.Join(",", arguments.Values.Cast<object?>()) : "";
    }

    private void MaybeFail(string at)
    {
        if (Environment.GetEnvironmentVariable("KRK_MODE") == at && scenario.ScenarioInfo.Tags.Contains(at))
            throw new InvalidOperationException($"{at} failed in {Who}");
    }

    [Given("a logged in customer")]
    public async Task ALoggedInCustomer()
    {
        await driver.Ran_for(Who);
        MaybeFail("fail-bg");
        MaybeFail("fail-bg2");
    }

    [Given("the address is checked")]
    public async Task TheAddressIsChecked()
    {
        await driver.Ran_for(Who);
        MaybeFail("fail-and");
    }

    [Given("the basket holds {string}")]
    public Task TheBasketHolds(string what) => driver.Ran_for(Who);

    [Given("the basket holds")]
    public Task TheBasketHoldsTable(DataTable table) => driver.Ran_for(Who + " table:" + table.Rows[0][0]);

    [Given("a note says")]
    public Task ANoteSays(string note) => driver.Ran_for(Who + " note:" + note);

    [Given("a composite step calls another")]
    public async Task ACompositeStepCallsAnother()
    {
        await driver.Ran_for(Who);
        await runner.GivenAsync("the basket holds \"from the composite\"");
    }

    [Given("a screenshot is attached")]
    public async Task AScreenshotIsAttached()
    {
        await driver.Ran_for(Who);
        var folder = Path.Combine(Path.GetTempPath(), "krk-shapes");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, Who.Replace(' ', '-') + ".txt");
        await File.WriteAllTextAsync(file, Who);
        if (Environment.GetEnvironmentVariable("KRK_ATTACH") == "1")
            output.AddAttachment(file);
    }

    [When("the customer acts {string}")]
    public async Task TheCustomerActs(string what)
    {
        await driver.Ran_for(Who);
        MaybeFail("fail-when");
    }

    [Then("the outcome is {string}")]
    public Task TheOutcomeIs(string what) => driver.Ran_for(Who);
}

public class ShapeDriver(HttpClient client)
{
    [Step]
    public async Task Ran_for(string who)
    {
        (await client.GetAsync("ran-for/" + Uri.EscapeDataString(who))).EnsureSuccessStatusCode();
    }
}
