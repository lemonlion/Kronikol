@retry-demo
Feature: Retry demo

  Scenario: A flaky scenario that passes on the second attempt
    Given a customer named "Flaky"
    When the flaky step runs
    Then the order is confirmed
