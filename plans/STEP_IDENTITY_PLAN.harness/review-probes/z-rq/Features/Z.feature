Feature: BG1 a Given fails in one scenario, the passing one first

Scenario: A passes
    Given the shop is ready
    When the customer orders
    Then the order is listed

@fail-given
Scenario: B fails at its Given
    Given the shop is ready
    When the customer orders
    Then the receipt is sent
