Feature: BG3 a plain hook fails in one scenario, the passing ones after it

@fail-hook
Scenario: A hook fails
    Given the shop is ready
    When the customer orders
    Then the order is listed

Scenario: B passes
    Given the shop is ready
    When the customer orders
    Then the receipt is sent

Scenario: C passes
    Given the shop is ready
    When the customer orders
    Then the invoice is sent
