namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ReachableReadRegionRegressionTests
{
    [Test]
    public void ReadsThroughConditionalAndHeapReceiversKeepTheirSourceRegions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            public sealed class Node {
                public int Value;
                public int Property { get; set; }
                public Node? Inner;
                public Node? Next;
            }

            public static class Holder {
                public static int Current;
            }

            public sealed class Sample {
                public int OwnValue;

                public static int ConditionalField(Node? value) =>
                    value?.Value ?? 0;

                public static int ConditionalProperty(Node? value) =>
                    value?.Property ?? 0;

                public static int NestedConditional(Node? value) =>
                    value?.Inner?.Value ?? 0;

                public static int NestedField(Node value) =>
                    value.Next!.Value;

                public static int ArrayElement(Node[] values) =>
                    values[0].Value;

                public static int ConditionalReceiver(
                    bool chooseFirst,
                    Node first,
                    Node second) =>
                    (chooseFirst ? first : second).Value;

                public int ReceiverAndParameter(Node? value) =>
                    value?.Value ?? OwnValue;

                public static int StaticControl() => Holder.Current;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertRead(
            session,
            compilation,
            "ConditionalField",
            EffectRegionId.Parameter(0));
        AssertRead(
            session,
            compilation,
            "ConditionalProperty",
            EffectRegionId.Parameter(0));
        AssertRead(
            session,
            compilation,
            "NestedConditional",
            EffectRegionId.Parameter(0));
        AssertRead(
            session,
            compilation,
            "NestedField",
            EffectRegionId.Parameter(0));
        AssertRead(
            session,
            compilation,
            "ArrayElement",
            EffectRegionId.Parameter(0));
        AssertRead(
            session,
            compilation,
            "ConditionalReceiver",
            EffectRegionId.Parameter(1),
            EffectRegionId.Parameter(2));
        AssertRead(
            session,
            compilation,
            "ReceiverAndParameter",
            EffectRegionId.Parameter(0),
            EffectRegionId.Receiver);
        AssertRead(
            session,
            compilation,
            "StaticControl",
            EffectRegionId.Static());
    }

    private static void AssertRead(
        EffectAnalysisSession session,
        Compilation compilation,
        string methodName,
        params EffectRegionId[] expectedRegions)
    {
        var summary = session.Analyze(
            EffectTestHost.SampleMethod(compilation, methodName)).Summary;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.Reads.IsUnknown, Is.False, methodName);
            Assert.That(
                summary.Reads.Regions,
                Is.EquivalentTo(expectedRegions),
                methodName);
        }
    }
}
