Feature: BG5 a hook step in every scenario with its own argument

  @seedalice
  Scenario: BG5a seeds alice
    Given the shop is ready
    When the customer places the order
    Then order 51 is read back

  @seedbob
  Scenario: BG5b seeds bob
    Given the shop is ready
    When the customer places the order
    Then order 52 is read back
