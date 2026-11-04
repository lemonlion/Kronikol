Feature: R8 background hooks

  Background:
    Given r8 setup one
    And r8 setup two

  Scenario: B01 passes
    When r8 action
    Then r8 check

  @fail-in-bg
  Scenario: B02 scenario step left open
    When r8 action~!
    Then r8 check
