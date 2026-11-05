Feature: BG11 one scenario skips itself in a hook

  @needsgpu
  Scenario: BG11a skips itself
    Given the shop is ready
    When the customer places the order
    Then order 111 is read back

  Scenario: BG11b passes
    Given the shop is ready
    When the customer places the order
    Then order 112 is read back

  Scenario: BG11c passes
    Given the shop is ready
    When the customer places the order
    Then order 113 is read back
