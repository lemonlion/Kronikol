Feature: BG6 a hook step that fails in one scenario

@seeded
Scenario: A hook step passes
    Given the shop is ready
    When the customer orders
    Then the order is listed

@fail-hook-step
Scenario: B hook step fails
    Given the shop is ready
    When the customer orders
    Then the receipt is sent
