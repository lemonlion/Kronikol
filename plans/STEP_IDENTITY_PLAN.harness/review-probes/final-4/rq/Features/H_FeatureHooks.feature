@beforefeaturethrows
Feature: H a BeforeFeature hook throws

  Scenario: H1 first under a throwing BeforeFeature
    Given a registered customer
    When the customer places the order

  Scenario: H2 second under a throwing BeforeFeature
    Given a registered customer
    When the customer places the order
