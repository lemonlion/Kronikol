@endpoint:/cake
Feature: Cake Quality
    As a dessert provider
    I want every cake baked from freshly fetched ingredients
    So that customers can trust what goes into their cakes

Background:
    Given a valid post request for the Cake endpoint

Scenario: The baked cake contains the requested milk
    When the request is sent to the cake post endpoint
    Then the cake should contain the requested milk

Scenario: The baked cake contains the requested flour
    When the request is sent to the cake post endpoint
    Then the cake should contain the requested flour
