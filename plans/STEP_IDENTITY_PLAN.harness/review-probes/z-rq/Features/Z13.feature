Feature: N3 a pending step, a runtime skip, an ignored scenario

Scenario: A pending
    Given the shop is ready
    When the step is pending
    Then the order is listed

Scenario: B skips itself
    Given the shop is ready
    When the scenario skips itself
    Then the receipt is sent

@ignore
Scenario: C ignored
    Given the shop is ready
    Then the invoice is sent
