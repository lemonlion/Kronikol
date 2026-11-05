Feature: G2 steps at depth in a background

  Background:
    Given A runs B which fails then C
    And the shop is ready

  Scenario: G21 inner failure in the background
    When the customer places the order
