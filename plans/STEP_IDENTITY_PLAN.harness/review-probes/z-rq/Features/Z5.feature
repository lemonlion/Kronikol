Feature: BG5 a hook step in every scenario with its own argument

@seed-a
Scenario: A seeds alice
    Given the shop is ready
    When the customer orders
    Then the order is listed

@seed-b
Scenario: B seeds bob
    Given the shop is ready
    When the customer orders
    Then the receipt is sent
