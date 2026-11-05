Feature: BG7 one hook step, failing in one scenario

  @seeded
  Scenario: BG7a its hook step passes
    Given the shop is ready
    When the customer places the order
    Then order 71 is read back

  @seedfails
  Scenario: BG7b its hook step fails
    Given the shop is ready
    When the customer places the order
    Then order 72 is read back
