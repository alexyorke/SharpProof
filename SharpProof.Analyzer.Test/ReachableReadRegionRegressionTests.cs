using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ReachableReadRegionRegressionTests
{
    [Test]
    public async Task PureContractsAllowReachableReadsButRejectStaticReads()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            #nullable enable
            using SharpProof.Attributes;

            public sealed class Node {
                public int Value;
                public int Property { get; set; }
                public Node? Inner;
                public Node? Next;
            }

            public static class Holder {
                public static int Current;
            }

            public sealed class Fixture {
                public int OwnValue;

                [EnforcePure]
                public static int ConditionalField(Node? value) =>
                    value?.Value ?? 0;

                [EnforcePure]
                public static int ConditionalProperty(Node? value) =>
                    value?.Property ?? 0;

                [EnforcePure]
                public static int NestedConditional(Node? value) =>
                    value?.Inner?.Value ?? 0;

                [EnforcePure]
                public static int NestedField(Node value) =>
                    value.Next!.Value;

                [EnforcePure]
                public static int ArrayElement(Node[] values) =>
                    values[0].Value;

                [EnforcePure]
                public static int ConditionalReceiver(
                    bool chooseFirst,
                    Node first,
                    Node second) =>
                    (chooseFirst ? first : second).Value;

                [EnforcePure]
                public int ReceiverAndParameter(Node? value) =>
                    value?.Value ?? OwnValue;

                [EnforcePure]
                public static int StaticControl() => Holder.Current;
            }
            """,
            "effects",
            ["SP0002"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0002");
        AnalyzerTestHost.AssertMessageContains(
            diagnostics.Single(),
            "StaticControl");
    }
}
