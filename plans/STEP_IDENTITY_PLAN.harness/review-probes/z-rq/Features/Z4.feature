Feature: BG4 a hook step on one scenario of a feature with a shared opening

@seeded
Scenario: A seeded
    Given the shop is ready
    When the customer orders
    Then the order is listed

Scenario: B not seeded
    Given the shop is ready
    When the customer orders
    Then the receipt is sent
