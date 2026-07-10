using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Test;

[NonParallelizable]
public sealed class StringCultureBehaviorTests
{
    [Test]
    public void StartsWith_String_DefaultOverload_FollowsCurrentCultureAcrossCultureStates()
    {
        var enUsDefault = WithCurrentCulture("en-US", () => "encyclop\u00E6dia".StartsWith("encyclopae"));
        var enUsCurrent = WithCurrentCulture("en-US",
            () => "encyclop\u00E6dia".StartsWith("encyclopae", StringComparison.CurrentCulture));
        var daDkDefault = WithCurrentCulture("da-DK", () => "encyclop\u00E6dia".StartsWith("encyclopae"));
        var daDkCurrent = WithCurrentCulture("da-DK",
            () => "encyclop\u00E6dia".StartsWith("encyclopae", StringComparison.CurrentCulture));

        Assert.That(enUsDefault, Is.EqualTo(enUsCurrent));
        Assert.That(daDkDefault, Is.EqualTo(daDkCurrent));
    }

    [Test]
    public void StartsWith_String_DefaultOverload_MatchesExplicitCurrentCulture()
    {
        Assert.That(WithCurrentCulture("en-US",
            () => "encyclop\u00E6dia".StartsWith("encyclopae") ==
                  "encyclop\u00E6dia".StartsWith("encyclopae", StringComparison.CurrentCulture)), Is.True);

        Assert.That(WithCurrentCulture("da-DK",
            () => "encyclop\u00E6dia".StartsWith("encyclopae") ==
                  "encyclop\u00E6dia".StartsWith("encyclopae", StringComparison.CurrentCulture)), Is.True);
    }

    [Test]
    public void StartsWith_String_OrdinalOverload_DoesNotChangeWithCurrentCulture()
    {
        var enUs = WithCurrentCulture("en-US",
            () => "encyclop\u00E6dia".StartsWith("encyclopae", StringComparison.Ordinal));
        var daDk = WithCurrentCulture("da-DK",
            () => "encyclop\u00E6dia".StartsWith("encyclopae", StringComparison.Ordinal));

        Assert.That(enUs, Is.False);
        Assert.That(daDk, Is.False);
    }

    [Test]
    public void Contains_String_DefaultOverload_MatchesExplicitOrdinalAcrossCultures()
    {
        Assert.That(WithCurrentCulture("en-US",
            () => "encyclop\u00E6dia".Contains("ae") ==
                  "encyclop\u00E6dia".Contains("ae", StringComparison.Ordinal)), Is.True);

        Assert.That(WithCurrentCulture("da-DK",
            () => "encyclop\u00E6dia".Contains("ae") ==
                  "encyclop\u00E6dia".Contains("ae", StringComparison.Ordinal)), Is.True);
    }

    private static T WithCurrentCulture<T>(string cultureName, Func<T> action)
    {
        var priorCulture = Thread.CurrentThread.CurrentCulture;
        var priorUiCulture = Thread.CurrentThread.CurrentUICulture;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = priorCulture;
            Thread.CurrentThread.CurrentUICulture = priorUiCulture;
        }
    }
}