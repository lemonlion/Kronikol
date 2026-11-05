Feature: Q4 a block hook's step method ahead of a step that did not open

  @t02 @q4
  Scenario: Q4 a block hook runs a step method and the When's BeforeStep hook throws
    Given a registered customer
    When the customer places the order
    Then the order is confirmed
