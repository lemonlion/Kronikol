Feature: What one scenario does to the others under a written Background

  Background:
    Given the shop is ready

  Scenario: WB1 passes ahead of the one that fails
    When an order of 61 is placed
    Then the order is confirmed

  @failgiven
  Scenario: WB2 fails in the Background
    When an order of 62 is placed
    Then the order is confirmed

  @failhook
  Scenario: WB3 a plain hook fails
    When an order of 63 is placed
    Then the order is confirmed

  @needsgpu
  Scenario: WB11 skips itself in a hook
    When an order of 64 is placed
    Then the order is confirmed

  @seedalice
  Scenario: WB5a a hook step with its own argument
    When an order of 65 is placed
    Then the order is confirmed

  @seedbob
  Scenario: WB5b a hook step with another argument
    When an order of 66 is placed
    Then the order is confirmed

  @seedfails
  Scenario: WB7 a hook step fails
    When an order of 67 is placed
    Then the order is confirmed

  Scenario: WB4 passes behind them all
    When an order of 68 is placed
    Then the order is confirmed
