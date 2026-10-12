using Kronikol.Reports;

namespace Kronikol.Extensions.Kafka.BuildInterception;

/// <summary>
/// Whether tiered compilation can recompile a patched <c>Build()</c> and so drop its patch, read the way the runtime
/// reads its own settings: for each of <c>TieredCompilation</c> and <c>TC_QuickJit</c>, the <c>DOTNET_</c> environment
/// variable, then the <c>COMPlus_</c> one, each a hexadecimal number taken from its leading digits (an optional
/// <c>0x</c>; a value with no digits is ignored), and only then the runtimeconfig value the project's MSBuild property
/// writes. Measured case by case (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.harness/audit/B/e5): the environment beats the
/// project, and <c>DOTNET_TC_QuickJit=false</c> reads as 0xFA, which turns quick JIT on.
/// </summary>
internal sealed class TieredCompilationCheck(
    Func<string, string?> environmentVariable,
    Func<string, object?> appContextData,
    Func<TextWriter> standardError)
{
    internal const string DiagnosticKey = "Kronikol.Extensions.Kafka.BuildInterception.TieredCompilation";

    internal const string Message =
        "Kafka build interception can stop after the first few dozen builds under tiered compilation (about 40 in a test " +
        "run with pauses), and every client built after that is untracked: add " +
        "<TieredCompilationQuickJit>false</TieredCompilationQuickJit> to the test project, or set DOTNET_TC_QuickJit=0, to keep it.";

    private int _reported;

    /// <summary>The check on this process's own settings, writing to <see cref="Console.Error"/>.</summary>
    internal static TieredCompilationCheck ForThisProcess() =>
        new(Environment.GetEnvironmentVariable, AppContext.GetData, () => Console.Error);

    /// <summary>True when the runtime can recompile a patched method, which loses its patch.</summary>
    internal bool CanLoseThePatch =>
        IsOn("TieredCompilation", "System.Runtime.TieredCompilation")
        && IsOn("TC_QuickJit", "System.Runtime.TieredCompilation.QuickJit");

    /// <summary>
    /// When <see cref="CanLoseThePatch"/>, writes <see cref="Message"/> to standard error and records it among the
    /// diagnostics of every report this process writes, once.
    /// </summary>
    internal void ReportOnce()
    {
        if (!CanLoseThePatch || Interlocked.Exchange(ref _reported, 1) == 1)
            return;

        standardError().WriteLine(Message);
        ProcessDiagnostics.RecordOnce(DiagnosticKey, DiagnosticKind.CaptureDegraded, Message);
    }

    // On unless switched off, the environment first.
    private bool IsOn(string setting, string runtimeConfigName) =>
        EnvironmentValue("DOTNET_" + setting) ?? EnvironmentValue("COMPlus_" + setting) ?? RuntimeConfigValue(runtimeConfigName) ?? true;

    private bool? EnvironmentValue(string name)
    {
        var value = environmentVariable(name);
        if (value is null)
            return null;

        var digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        var length = 0;
        while (length < digits.Length && Uri.IsHexDigit(digits[length]))
            length++;

        return length == 0 ? null : digits.AsSpan(0, length).IndexOfAnyExcept('0') >= 0;
    }

    private bool? RuntimeConfigValue(string name) => appContextData(name) switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var value) => value,
        _ => null,
    };
}
