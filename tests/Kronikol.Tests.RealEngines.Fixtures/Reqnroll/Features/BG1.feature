Feature: BG1 a Given fails in one scenario, the passing one first

  Scenario: BG1a passes
    Given the shop is ready
    When the customer places the order
    Then order 11 is read back

  @failgiven
  Scenario: BG1b fails at the step they share
    Given the shop is ready
    When the customer places the order
    Then order 12 is read back
