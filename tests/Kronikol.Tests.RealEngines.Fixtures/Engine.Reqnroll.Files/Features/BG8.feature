@screenshotonfailure
Feature: BG8 a failure screenshot at the step they share, with no Background written

  @bgfails
  Scenario: BG8a fails at the step they share
    Given a customer that fails when tagged
    And an order of 81 is placed
    Then the order is confirmed

  Scenario: BG8b passes
    Given a customer that fails when tagged
    And an order of 82 is placed
    Then the order is confirmed
