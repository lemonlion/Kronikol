Feature: A Background That Fails
    Its step fails only when the Integration project asks, with KRONIKOL_EXAMPLE_STEP_FAILURES.

Background:
    Given a Background step that fails when asked

Scenario: The Background step is the one that failed
    When another step that passes
