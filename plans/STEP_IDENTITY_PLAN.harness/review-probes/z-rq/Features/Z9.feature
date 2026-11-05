Feature: BG7 one hook step, failing in one scenario

@seed-maybe
Scenario: A hook step passes
    Given the shop is ready
    When the customer orders
    Then the order is listed

@seed-maybe @seed-fails
Scenario: B hook step fails
    Given the shop is ready
    When the customer orders
    Then the receipt is sent
