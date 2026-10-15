@background
Feature: Background probe

  Background:
    Given the page is open

  Scenario: A failing scenario under a background
    When a step attaches a named image
    Then the check fails
