@feature-tag @category:demo
Feature: Kronikol demo feature

  This feature exercises every Gherkin construct the Kronikol
  Cucumber Messages importer has to map.

  Background:
    Given the catalogue is loaded

  @happy-path @endpoint:/api/orders
  Scenario: A simple passing scenario

    The canonical happy path: one customer, one order.

    Given a customer named "Ada"
    When the customer places an order
    And the customer opens the "overview" page
    Then the order is confirmed
    But the order is confirmed

  Rule: Orders must be validated

    @category:validation
    Scenario: A scenario with a data table and a doc string
      Given the following order lines:
        | sku     | quantity | price |
        | APPLE-1 | 2        | 1.50  |
        | PEAR-7  | 1        | 2.25  |
      When the payload is submitted:
        """json
        { "channel": "web", "currency": "GBP" }
        """
      Then the order is confirmed

    @failing
    Scenario: A failing scenario
      Given a customer named "Ada"
      When the step blows up
      Then the order is confirmed

  @category:demo
  Scenario Outline: An outline over pages
    Given a customer named "<customer>"
    When the customer opens the "<page>" page
    Then the order is confirmed

    Examples:
      | customer | page      |
      | Ada      | overview  |
      | Grace    | customers |
