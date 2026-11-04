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
