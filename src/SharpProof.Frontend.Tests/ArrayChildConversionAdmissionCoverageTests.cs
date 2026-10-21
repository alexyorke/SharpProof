using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
internal sealed class ArrayChildConversionAdmissionCoverageTests
{
    [TestCase("receiver", FrontendAbstention.UnsupportedOperationKind)]
    [TestCase("index", FrontendAbstention.ConversionMayChangeValue)]
    [TestCase("compound", FrontendAbstention.ConversionMayChangeValue)]
    public void ActualArrayChildConversionsPreserveClosureAndTypedParameterControls(string route, FrontendAbstention expected)
    {
        System.ArgumentNullException.ThrowIfNull(route);
        var source = route switch
        {
            "receiver" => "bool Target(object values) => ((float[])values)[0] == 1f;",
            "index" => "bool Target(float[] values, object index) => values[(int)index] == 1f;",
            _ => "bool Target(int[] values, object next) { values[0] += (int)next; return values[0] == 3; }"
        };
        using var rejected = TypedProgramSubject.Create(source);
        ImmutableArray<object> arguments = route == "compound" ? [new[] { 1 }, 2]
            : route == "index" ? [new[] { 1f }, 0] : [new[] { 1f }];
        Assert.That(rejected.Invoke(arguments), Is.True);
        if (route == "compound")
        { Assert.That(((int[])arguments[0])[0], Is.EqualTo(3)); }
        var lowering = rejected.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(expected));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);

        var control = route switch
        {
            "receiver" => "bool Target(float[] values) => values[0] == 1f;",
            "index" => "bool Target(float[] values, int index) => values[index] == 1f;",
            _ => "bool Target(int[] values, int next) { values[0] += next; return values[0] == 3; }"
        };
        using var supported = TypedProgramSubject.Create(control);
        ImmutableArray<object> controlArguments = route == "compound" ? [new[] { 1 }, 2]
            : route == "index" ? [new[] { 1f }, 0] : [new[] { 1f }];
        Assert.That(supported.Invoke(controlArguments), Is.True);
        if (route == "compound")
        { Assert.That(((int[])controlArguments[0])[0], Is.EqualTo(3)); }
        Assert.That(supported.LowerSourceCalls(false, opaqueCalls: true).IsExact, Is.True);
    }
}
