Feature: BG2 a Given fails in one scenario, the failing one first

@fail-given
Scenario: A fails at its Given
    Given the shop is ready
    When the customer orders
    Then the order is listed

Scenario: B passes
    Given the shop is ready
    When the customer orders
    Then the receipt is sent
