Feature: BG2 a Given fails in one scenario, the failing one first

  @failgiven
  Scenario: BG2a fails at the step they share
    Given the shop is ready
    When the customer places the order
    Then order 21 is read back

  Scenario: BG2b passes
    Given the shop is ready
    When the customer places the order
    Then order 22 is read back
