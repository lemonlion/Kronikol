@screenshot
Feature: BG10 screenshot on failure inside the shared opening failing first

@fail-when
Scenario: A fails at its When with a screenshot
    Given the shop is ready
    When the customer orders
    Then the order is listed

Scenario: B passes
    Given the shop is ready
    When the customer orders
    Then the receipt is sent
