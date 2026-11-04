Feature: Retried

@retry(2)
Scenario: Q01 a retried scenario with a handler per attempt
    Given a call named q01-given
    When a call named q01-when
    Then the first attempt of q01 fails
    And a call named q01-then

@retry(2)
@shared-handler
Scenario: Q02 a retried scenario with a shared handler
    Given a call named q02-given
    When a call named q02-when
    Then the first attempt of q02 fails
    And a call named q02-then
