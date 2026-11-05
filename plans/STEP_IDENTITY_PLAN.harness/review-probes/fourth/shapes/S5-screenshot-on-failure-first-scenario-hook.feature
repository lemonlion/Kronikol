@screenshotonfailure
Feature: S5 a failure screenshot in an AfterStep hook, the first scenario fails in its opening step, a hook step in it

  Background:
    Given a customer that fails when tagged

  @seeded @bgfails
  Scenario: S5a fails in the background
    And an order of 1 is placed
    Then the order is confirmed

  Scenario: S5b passes
    And an order of 2 is placed
    Then the order is confirmed
