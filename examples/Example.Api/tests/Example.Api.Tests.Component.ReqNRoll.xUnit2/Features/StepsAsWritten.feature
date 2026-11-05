@reads-the-public-list
Feature: Steps As Written
    A Background is what this file writes, and a step a step definition runs is that step's sub-step.

Background:
    Given a step that passes
    And a step runs a step with a table and a doc string

Scenario: A scenario's own steps come after its Background
    When another step that passes
    Then a third step that passes

Rule: A rule's Background follows its feature's

    Background:
        Given a rule's step that passes

    Scenario: A scenario in a rule runs both Backgrounds
        When another step that passes
