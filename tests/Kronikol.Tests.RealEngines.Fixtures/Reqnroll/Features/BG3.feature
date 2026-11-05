Feature: BG3 a plain hook fails in one scenario

  @failhook
  Scenario: BG3a a plain hook fails
    Given the shop is ready
    When the customer places the order
    Then order 31 is read back

  Scenario: BG3b passes
    Given the shop is ready
    When the customer places the order
    Then order 32 is read back

  Scenario: BG3c passes
    Given the shop is ready
    When the customer places the order
    Then order 33 is read back
