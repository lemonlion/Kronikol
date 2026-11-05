Feature: BG11 a green run, one scenario skips itself in a hook

@needs-gpu
Scenario: A needs a GPU
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
