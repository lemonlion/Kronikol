@featurerunsstep
Feature: H2 a BeforeFeature hook runs a Gherkin step

  Scenario: H21 under a feature hook that ran a step
    Given a registered customer
    When the customer places the order
