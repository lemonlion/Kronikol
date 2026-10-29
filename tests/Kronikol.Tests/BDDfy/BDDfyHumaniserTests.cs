using System.Reflection;
using Kronikol.BDDfy.xUnit3;
using TestStack.BDDfy.Configuration;

namespace Kronikol.Tests.BDDfy;

/// <summary>
/// Which humaniser makes BDDfy's own titles (plans/CLIENT_FLOORS_PLAN.md F32). 8.0.11 titles with
/// <c>Configurator.Humanizer</c> and keeps <c>Scanners.Humanize</c> only as an obsolete static copy of the humaniser
/// configured when the field is first read, for the rest of the run, while 8.0.1.3, the release the adapter asks for,
/// has that field alone, on the instance. A title made by a humaniser the test sets is BDDfy's own on either, whenever
/// it is set: the adapter that read that copy, on 8.0.12, failed the default's fact after a fact that set a humaniser,
/// in one run. The suite runs on the newest 8.x, and on 8.0.1.3 with <c>-p:FloorRun=true</c>, so these facts set the
/// humaniser by reflection, as the adapter reads it.
/// </summary>
[Collection(BDDfyHumaniserCollection.Name)]
public class BDDfyHumaniserTests
{
    private static string Shouted(string name) => "Shouted " + name.ToUpperInvariant();

    [Fact]
    public void A_scenario_title_the_configured_humaniser_made_is_BDDfy_s_own()
    {
        using (Humaniser.Set(Shouted))
        {
            Assert.True(DiagramCapturingProcessor.IsOwnTitle("Shouted WHENMCAFUNDINGACTIVATES", "FundingTests", "WhenMCAFundingActivates"));
            Assert.True(DiagramCapturingProcessor.IsOwnTitle("Shouted FUNDINGTESTS", "FundingTests", "WhenMCAFundingActivates"));
            // what the default humaniser makes is a title the test wrote while another humaniser is set
            Assert.False(DiagramCapturingProcessor.IsOwnTitle("When m c a funding activates", "FundingTests", "WhenMCAFundingActivates"));
        }
    }

    [Fact]
    public void A_story_title_the_configured_humaniser_made_is_BDDfy_s_own()
    {
        using (Humaniser.Set(Shouted))
            Assert.True(DiagramCapturingProcessor.IsOwnStoryTitle(new TestStack.BDDfy.StoryMetadata(typeof(BDDfyHumaniserTests), "n1", "n2", "n3")));
    }

    [Fact]
    public void With_no_humaniser_set_the_default_s_titles_are_BDDfy_s_own()
    {
        Assert.True(DiagramCapturingProcessor.IsOwnTitle("When m c a funding activates", "FundingTests", "WhenMCAFundingActivates"));
        Assert.False(DiagramCapturingProcessor.IsOwnTitle("Shouted WHENMCAFUNDINGACTIVATES", "FundingTests", "WhenMCAFundingActivates"));
    }

    /// <summary>Sets the humaniser the running BDDfy titles with, and puts the one before back when disposed.</summary>
    private static class Humaniser
    {
        public static IDisposable Set(Func<string, string> humanise)
        {
            var configured = typeof(Configurator).GetProperty("Humanizer", BindingFlags.Public | BindingFlags.Static);
            if (configured is not null)
            {
                var previous = configured.GetValue(null);
                configured.SetValue(null, HumaniserProxy.Create(configured.PropertyType, humanise));
                return new Undo(() => configured.SetValue(null, previous));
            }
            var field = typeof(Scanners).GetField("Humanize", BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)!;
            var target = field.IsStatic ? null : Configurator.Scanners;
            var old = field.GetValue(target);
            field.SetValue(target, humanise);
            return new Undo(() => field.SetValue(target, old));
        }

        private sealed class Undo(Action undo) : IDisposable
        {
            public void Dispose() => undo();
        }
    }
}

/// <summary>An <c>IHumanizer</c> (BDDfy 8.0.11) over a function, made at run time, as 8.0.1.3 has no such interface.</summary>
public class HumaniserProxy : DispatchProxy
{
    private Func<string, string> _humanise = name => name;

    public static object Create(Type contract, Func<string, string> humanise)
    {
        var proxy = DispatchProxy.Create(contract, typeof(HumaniserProxy));
        ((HumaniserProxy)proxy)._humanise = humanise;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == "Humanize" ? _humanise((string)args![0]!) : throw new NotSupportedException(targetMethod?.Name);
}

/// <summary>
/// The humaniser is BDDfy's process state: a BDDfy title made beside a fact that sets one would come out of that one.
/// Here nothing runs beside it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BDDfyHumaniserCollection
{
    public const string Name = "BDDfyHumaniser";
}
