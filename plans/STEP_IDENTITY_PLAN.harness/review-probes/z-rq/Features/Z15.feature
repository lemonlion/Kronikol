@attach-always
Feature: BG12 a green run, a file after every step, one scenario skips itself in a hook

Scenario: A passes
    Given the shop is ready
    When the customer orders
    Then the order is listed

@needs-gpu
Scenario: B needs a GPU
    Given the shop is ready
    When the customer orders
    Then the receipt is sent
