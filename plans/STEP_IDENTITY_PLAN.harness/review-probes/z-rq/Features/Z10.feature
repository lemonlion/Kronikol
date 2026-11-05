@screenshot
Feature: BG8 screenshot on failure failing first

@fail-given
Scenario: A fails at its Given with a screenshot
    Given the shop is ready
    When the customer orders
    Then the order is listed

Scenario: B passes
    Given the shop is ready
    When the customer orders
    Then the receipt is sent
