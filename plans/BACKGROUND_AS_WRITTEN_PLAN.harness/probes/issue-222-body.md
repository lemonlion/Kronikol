**Found:** 2026-10-30, adding composite steps (as LightBDD calls them) to a Reqnroll suite: each step definition calls `[Step]` methods (Kronikol.StepTracking), which the report draws as that Gherkin step's sub-steps. Two scenarios that start with the same steps showed one customer in their sub-steps, though each had registered its own.

**Versions:** Kronikol, Kronikol.ReqNRoll.xUnit2, Kronikol.StepTracking and Kronikol.AssertionTracking 5.27.13 (`5.27.13+c58d98c0`) · Reqnroll / Reqnroll.xUnit 3.3.4 · xunit.assert 2.9.3, xunit.runner.visualstudio 3.1.5 · Shouldly 4.3.0 · net10.0 (SDK 10.0.403) · macOS 26.7.1, arm64. `BackgroundStepsDetector` is the same on main (27cc0e46).

---

## What happens

`BackgroundStepsDetector` takes the steps a feature's scenarios share at their start, matched on keyword and text, as their background, and hands every scenario the first scenario's step objects for them:

https://github.com/lemonlion/Kronikol/blob/27cc0e4612a1d36754da65e2bd23787ffed1e271/src/Kronikol/Reports/BackgroundStepsDetector.cs#L44-L51

So every scenario after the first shows the first scenario's copy of those steps: its result, duration, attachments, table and doc string (only the text is compared), and its sub-steps with their parameters and assertions. Its own are dropped.

In a Reqnroll feature that happens wherever every scenario's first step after the shared ones is a `Then`, `And` or `But`, or there is none. Two cases are common: scenarios that share their `Given` and `When` and differ in their `Then`, and a Scenario Outline whose placeholders are only in a table or doc string, so that every row's step texts are the same.

### Two scenarios that share their `Given` and `When`

The second scenario's payment is declined:

```gherkin
Feature: Shared prefix

@happy-path
Scenario: The first scenario's order goes through
    Given the basket holds
        | sku   | qty |
        | APPLE | 1   |
    When the customer places the order
    Then the order is confirmed

@declined
Scenario: The second scenario's payment is declined
    Given the basket holds
        | sku    | qty |
        | BANANA | 2   |
    When the customer places the order
    Then the order is rejected
```

`kronikol query steps` for the second scenario:

```
s9  Shared prefix › The second scenario's payment is declined  [Failed]  0.01s
  b0    Given the basket holds  0.03s  [i0-i4] 3 calls
      · table
      · sku
      · qty
      · APPLE
      · 1
    b0.0  A customer is registered  0.02s
        · name
        · customer-5
      b0.0.0 Their address is verified  0.01s
      · b0.0.0.0 Response is success status code should be true
    b0.1  An item is added  0s
        · sku
        · APPLE
        · qty
        · 1
  b1    When the customer places the order  0.01s  [i6]
    b1.0  The payment is authorised  0.01s
        · amount
        · 99.99
        · card
        · 4111
    · b1.0.0 Response status code should be OK
    b1.1  The order is submitted
```

and the calls it made (`kronikol query interactions <reports> s9 --group-by method,path`):

```
method  path                                  calls errors   median      max bodies     bytes
POST    /customers                                1      0     0 ms     0 ms      1      24 B
GET     /customers/customer-10/address-check      1      0     5 ms     5 ms      1       2 B
PUT     /basket/customer-10/items/BANANA          1      0     0 ms     0 ms      1      26 B
POST    /payments                                 1      1     0 ms     0 ms      1      59 B
4 buckets
```

Every step is the first scenario's: the APPLE table, customer-5, card 4111 and a `When` that passed. This scenario added BANANA for customer-10, and its `POST /payments` was declined with a 402, which failed the `When` and the scenario, so the scenario failed with no failed step in its list. Its skipped `Then` is missing for a reason of its own: a step that Reqnroll skips after a failure isn't in the report.

### A Scenario Outline whose placeholders are in a table

```gherkin
Feature: Outline table

Scenario Outline: An order line for <sku>
    Given the basket holds
        | sku   | qty   |
        | <sku> | <qty> |
    When the customer places the order
    Then the order is confirmed

Examples:
    | sku    | qty |
    | APPLE  | 1   |
    | BANANA | 2   |
```

All three steps become background, and the BANANA row shows the APPLE row's table, customer (customer-7) and item in each of them. The row's own calls were to customer-8, ending with `PUT /basket/customer-8/items/BANANA`.

## Suggestion

Give each scenario its own steps: `scenario.BackgroundSteps = scenario.Steps!.Take(commonPrefixLength).ToArray()` in place of `members[0]`'s. The shared steps are still found on keyword and text, so the background marking, the `b0`, `b1` addresses and the layout stay as they are. Only whose results and arguments are drawn changes.

<details>
<summary>The suite (10 scenarios, two of which fail on purpose: the declined payments)</summary>

`dotnet test -c Release`, then `kronikol query steps <reports> sN` (or `interactions`, `flow`) on `bin/Release/net10.0/Reports`.

`Spike.Substeps.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Kronikol.ReqNRoll.xUnit2" Version="5.27.13" />
    <PackageReference Include="Kronikol.StepTracking" Version="5.27.13" PrivateAssets="all" />
    <PackageReference Include="Kronikol.AssertionTracking" Version="5.27.13" PrivateAssets="all" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="Reqnroll.xUnit" Version="3.3.4" />
    <PackageReference Include="Reqnroll.Tools.MsBuild.Generation" Version="3.3.4" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
    <PackageReference Include="xunit.assert" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <None Include="reqnroll.json" />
    <AdditionalFiles Include="reqnroll.json" />
  </ItemGroup>

</Project>
```

`reqnroll.json`

```json
{
  "$schema": "https://schemas.reqnroll.net/reqnroll-config-latest.json",
  "bindingAssemblies": [
    { "assembly": "Kronikol.ReqNRoll.xUnit2" }
  ]
}
```

`AssemblyInfo.cs`

```csharp
using Kronikol.Tracking;

[assembly: TrackSteps]
[assembly: TrackAssertions]
```

`Features/Orders.feature`

```gherkin
Feature: Orders

@happy-path
Scenario: A customer's order is confirmed
    Given a registered customer with a full basket
    When the customer places the order
    Then the order is confirmed

Scenario: A customer's order sends a confirmation email
    Given a registered customer with a full basket
    When the customer places the order
    Then a confirmation email is sent
```

`Features/OutlineTable.feature`

```gherkin
Feature: Outline table

Scenario Outline: An order line for <sku>
    Given the basket holds
        | sku   | qty   |
        | <sku> | <qty> |
    When the customer places the order
    Then the order is confirmed

Examples:
    | sku    | qty |
    | APPLE  | 1   |
    | BANANA | 2   |
```

`Features/Payments.feature`

```gherkin
Feature: Payments

Scenario: A declined card rejects the order
    Given a registered customer with a full basket
    When the customer pays with a declined card
    Then the order is rejected
```

`Features/Phases.feature`

```gherkin
Feature: Phases

Scenario: A Given sub-step inside a When step
    Given a registered customer with a full basket
    When the customer applies a discount through a Given sub-step
    Then the order is confirmed
```

`Features/Reuse.feature`

```gherkin
Feature: Reuse

Scenario: A step definition that is also a woven step
    Given a registered customer
    When the customer places the order
    Then the order is confirmed
```

`Features/Seeding.feature`

```gherkin
@seeded
Feature: Seeding

Scenario: Reference data is seeded by a hook
    Given a registered customer with a full basket
    When the customer places the order
    Then the order is confirmed
```

`Features/SharedPrefix.feature`

```gherkin
Feature: Shared prefix

@happy-path
Scenario: The first scenario's order goes through
    Given the basket holds
        | sku   | qty |
        | APPLE | 1   |
    When the customer places the order
    Then the order is confirmed

@declined
Scenario: The second scenario's payment is declined
    Given the basket holds
        | sku    | qty |
        | BANANA | 2   |
    When the customer places the order
    Then the order is rejected
```

`Steps/OrderSteps.cs`

```csharp
using Kronikol.Tracking;
using Reqnroll;
using Shouldly;

namespace Spike.Substeps.Steps;

[Binding]
public class OrderSteps(ShopDriver shop, ScenarioContext scenario)
{
    [Given("a registered customer with a full basket")]
    public async Task GivenARegisteredCustomerWithAFullBasket()
    {
        await shop.A_customer_is_registered(ShopDriver.NextCustomerName());
        await shop.AddItems(3);
    }

    [Given("the basket holds")]
    public async Task GivenTheBasketHolds(DataTable table)
    {
        await shop.A_customer_is_registered(ShopDriver.NextCustomerName());
        foreach (var row in table.Rows)
            await shop.An_item_is_added(row["sku"], int.Parse(row["qty"]));
    }

    [Given("a registered customer")]
    [Step]
    public Task A_registered_customer() => shop.A_customer_is_registered(ShopDriver.NextCustomerName());

    [When("the customer places the order")]
    public async Task WhenTheCustomerPlacesTheOrder()
    {
        await shop.The_payment_is_authorised(99.99m, scenario.ScenarioInfo.Tags.Contains("declined") ? "declined" : "4111");
        await shop.The_order_is_submitted();
        Console.WriteLine($"PHASE plain When step: {TestPhaseContext.Current}");
    }

    [When("the customer pays with a declined card")]
    public async Task WhenTheCustomerPaysWithADeclinedCard()
    {
        await shop.The_payment_is_authorised(99.99m, "declined");
        await shop.The_order_is_submitted();
    }

    [When("the customer applies a discount through a Given sub-step")]
    public async Task WhenTheCustomerAppliesADiscountThroughAGivenSubStep()
    {
        await shop.A_discount_code_is_applied("SAVE10");
        await shop.The_order_is_submitted();
        Console.WriteLine($"PHASE after a [GivenStep] sub-step in a When step: {TestPhaseContext.Current}");
    }

    [Then("the order is confirmed")]
    public void ThenTheOrderIsConfirmed() => shop.OrderStatus.ShouldBe("Confirmed");

    [Then("a confirmation email is sent")]
    public void ThenAConfirmationEmailIsSent() => shop.The_confirmation_email_is_queued();

    [Then("the order is rejected")]
    public void ThenTheOrderIsRejected() => shop.OrderStatus.ShouldBe("Rejected");
}
```

`Steps/ShopDriver.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using Kronikol.Tracking;
using Shouldly;

namespace Spike.Substeps.Steps;

/// <summary>The sub-steps: plain methods woven by Kronikol.StepTracking, called from Reqnroll step definitions.</summary>
public class ShopDriver(HttpClient client)
{
    private static int _customers;

    public static string NextCustomerName() => $"customer-{Interlocked.Increment(ref _customers)}";

    public string? CustomerId { get; private set; }
    public string? OrderStatus { get; private set; }

    [Step]
    public async Task A_customer_is_registered(string name)
    {
        var response = await client.PostAsJsonAsync("customers", new { name });
        response.EnsureSuccessStatusCode();
        CustomerId = name;
        await Their_address_is_verified();
    }

    [Step]
    public async Task Their_address_is_verified()
    {
        var response = await client.GetAsync($"customers/{CustomerId}/address-check");
        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Step(Description = "Items are added to the basket")]
    public async Task AddItems(int count)
    {
        for (var i = 0; i < count; i++)
            (await client.PutAsJsonAsync($"basket/{CustomerId}/items/{i}", new { sku = $"SKU-{i}" })).EnsureSuccessStatusCode();
    }

    [Step]
    public async Task An_item_is_added(string sku, int qty)
    {
        (await client.PutAsJsonAsync($"basket/{CustomerId}/items/{sku}", new { sku, qty })).EnsureSuccessStatusCode();
    }

    [Step]
    public async Task The_payment_is_authorised(decimal amount, string card)
    {
        var response = await client.PostAsJsonAsync("payments", new { amount, card });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Step]
    public async Task The_order_is_submitted()
    {
        var response = await client.PostAsJsonAsync("orders", new { customer = CustomerId });
        OrderStatus = (await response.Content.ReadFromJsonAsync<OrderResult>())!.Status;
    }

    [Step]
    public void The_confirmation_email_is_queued()
    {
        OrderStatus.ShouldBe("Confirmed");
    }

    [GivenStep]
    public async Task A_discount_code_is_applied(string code)
    {
        (await client.GetAsync($"discounts/{code}")).EnsureSuccessStatusCode();
    }

    [Step]
    public async Task Reference_data_is_seeded()
    {
        (await client.PostAsync("seed", null)).EnsureSuccessStatusCode();
    }

    private sealed record OrderResult(string Status);
}
```

`Hooks/Hooks.cs`

```csharp
using System.Net;
using System.Text;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.BoDi;
using Spike.Substeps.Steps;

namespace Spike.Substeps.Hooks;

[Binding]
public class Hooks
{
    [BeforeScenario(Order = 0)]
    public void RegisterTrackedClient(IObjectContainer container)
    {
        var handler = new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
        {
            CallerName = "Shop Tests",
            FixedNameForReceivingService = "Shop API"
        }) { InnerHandler = new FakeShopApi() };
        container.RegisterInstanceAs(new HttpClient(handler) { BaseAddress = new Uri("http://shop.local/") });
    }

    [BeforeScenario("seeded")]
    public Task SeedReferenceData(ShopDriver shop) => shop.Reference_data_is_seeded();

    [AfterTestRun]
    public static void WriteReports() =>
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Sub-step spike" });

    private sealed class FakeShopApi : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            var (status, json) = path switch
            {
                "/payments" when body.Contains("declined") => (HttpStatusCode.PaymentRequired, """{"error":"card declined"}"""),
                "/orders" => (HttpStatusCode.OK, """{"status":"Confirmed"}"""),
                _ => (HttpStatusCode.OK, "{}")
            };
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
```

</details>

