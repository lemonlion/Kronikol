Feature: Skipped

Scenario: A skipped sub-step under a Gherkin step
    Given a step skips an async sub-step and goes on
    When a later step runs

Scenario: A skipped sync sub-step under a Gherkin step
    Given a step skips a sync sub-step and goes on
    When a later step runs
