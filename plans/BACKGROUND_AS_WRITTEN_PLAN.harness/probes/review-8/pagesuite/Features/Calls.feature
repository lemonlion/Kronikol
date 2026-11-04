@calls
Feature: R8 calls

  Background:
    Given p8 setup call

  Scenario: CallPlain plainword
    When p8 call with plainword
    Then p8 done

  Scenario: CallPlainTwo plaintwoword
    When p8 call with plaintwoword
    Then p8 done

  Scenario Outline: CallRows
    When p8 call with <word>
    Then p8 done

    Examples:
      | word      |
      | firstword |
      | alphaword |
      | betaword  |
