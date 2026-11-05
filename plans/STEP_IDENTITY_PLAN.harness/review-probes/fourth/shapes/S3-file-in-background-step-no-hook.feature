Feature: S3 the control a file in a background step, no hook step

  Background:
    Given a customer with a screenshot

  Scenario: S3a
    And an order of 1 is placed
    Then the order is confirmed

  Scenario: S3b
    And an order of 2 is placed
    Then the order is confirmed
