Feature: R8 background left open

  Background:
    Given r8 setup one~!
    And r8 setup two

  @z-read
  Scenario: B03 background step left open
    When r8 action
    Then r8 check

  Scenario: B04 background step left open, second scenario
    When r8 other
