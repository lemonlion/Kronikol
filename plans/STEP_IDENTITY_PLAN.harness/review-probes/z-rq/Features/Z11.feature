@screenshot
Feature: BG9 screenshot on failure failing second

Scenario: A passes
    Given the shop is ready
    When the customer orders
    Then the order is listed

@fail-given
Scenario: B fails at its Given with a screenshot
    Given the shop is ready
    When the customer orders
    Then the receipt is sent
