"""The follow-up patch's mutants."""
SI = 'src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs'
EX = 'src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs'
SE = 'src/Kronikol.BDDfy.xUnit3/StepExtensions.cs'
MUTANTS = {
    'b01': {'desc': 'any failed row of any call decides', 'edits': [(SI, 'ofOneTest.Where(row => row.Call == lastCall).MaxBy(row => row.Result)!', 'ofOneTest.MaxBy(row => row.Result)!')]},
    'b02': {'desc': "the last call's first row decides", 'edits': [(SI, 'ofOneTest.Where(row => row.Call == lastCall).MaxBy(row => row.Result)!', 'ofOneTest.Where(row => row.Call == lastCall).First()')]},
    'b03': {'desc': 'the first row decides, as published', 'edits': [(SI, 'var deciding = Deciding(ofThisTest);', 'var deciding = x;')]},
    'b04': {'desc': "the steps' time does not count", 'edits': [(SI, 'var longest = steps > bdDfys ? steps : bdDfys;', 'var longest = bdDfys;')]},
    'b05': {'desc': "BDDfy's time does not count", 'edits': [(SI, 'var longest = steps > bdDfys ? steps : bdDfys;', 'var longest = steps;')]},
    'b06': {'desc': "a step reads BDDfy's time alone", 'edits': [(SI, 'Duration = Longest(step.Duration, collected?.Duration),', 'Duration = step.Duration,')]},
    'b07': {'desc': 'a test ends with its first row', 'edits': [(SI, 'EndedAt = ofThisTest.Max(row => row.EndedAt),', 'EndedAt = x.EndedAt,')]},
    'b08': {'desc': 'a title with no keyword reads Step', 'edits': [(SE, 'new BDDfyStepInfo(WordOf(step.ExecutionOrder), title,', 'new BDDfyStepInfo("Step", title,')]},
    'b09': {'desc': 'a bar draws the word it resolved', 'edits': [(EX, 'var word = WordOf(step, keyword);', 'var word = keyword;')]},
    'b10': {'desc': 'a But reads And', 'edits': [(EX, '            : title.StartsWith("But ", StringComparison.OrdinalIgnoreCase) ? "But"\n', '')]},
    'b11': {'desc': "a step's phase reads its word", 'edits': [(EX, 'StepCollector.StartFrame(testId, word, stepText, phaseKeyword: keyword)', 'StepCollector.StartFrame(testId, word, stepText, phaseKeyword: word)')]},
    'b12': {'desc': 'every call of a run is one call', 'edits': [('src/Kronikol.BDDfy.xUnit3/DiagramCapturingProcessor.cs', 'var call = Interlocked.Increment(ref _lastCall);', 'var call = 0;')]},
    'b13': {'desc': 'a skip reads Failed on the scenario', 'edits': [(SI, 'var skipped = StepExtensions.SkipReasonIn(deciding.ErrorMessage) is not null;', 'var skipped = false;')]},
    'b14': {'desc': 'a skip reads Failed on its step', 'edits': [(SI, 'Status = step.SkipReason is not null ? ExecutionResult.Skipped : step.Result.ToStepResult(priorFailure),', 'Status = step.Result.ToStepResult(priorFailure),')]},
    'b15': {'desc': "a skip's reason is dropped", 'edits': [(SI, '                BypassReason = step.SkipReason,\n', '')]},
    'b16': {'desc': 'a skip counts as a failure for the steps after it', 'edits': [(SI, 'if (step.Result == TestStack.BDDfy.Result.Failed && step.SkipReason is null)', 'if (step.Result == TestStack.BDDfy.Result.Failed)')]},
    'b17': {'desc': 'a skip keeps its error on the scenario', 'edits': [(SI, 'ErrorMessage = skipped ? null : deciding.ErrorMessage,', 'ErrorMessage = deciding.ErrorMessage,')]},
}
