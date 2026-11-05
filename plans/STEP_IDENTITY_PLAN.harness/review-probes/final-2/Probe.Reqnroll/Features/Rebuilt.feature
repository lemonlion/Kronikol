Feature: Rebuilt rows under a Background

  Background:
    Given a step that runs another step
    And the shop is ready

  Scenario: Q3c a row a consumer rebuilt in a scenario whose Background has a nested step
    When the customer places the order
    Then the order is confirmed
