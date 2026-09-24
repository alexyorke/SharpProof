using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ManagedStructReceiverEffectRegressionTests
{
    [Test]
    public async Task ManagedReceiverCopiesCannotPassEnforcePure()
    {
        var external = AnalyzerTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public interface IManagedMutator
            {
                void Mutate();
            }

            public struct ManagedValue : IManagedMutator
            {
                public int[] Items;

                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.ReadsReceiverState |
                        SharpProofEffect.WritesReceiverState,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public void Mutate() => Items[0]++;
            }

            public struct ScalarValue
            {
                public int Value;

                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.ReadsReceiverState |
                        SharpProofEffect.WritesReceiverState,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public void Mutate() => Value++;
            }

            public sealed class Owner
            {
                public readonly ManagedValue Value;
            }

            public static class ExternalMutator
            {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.WritesArgumentState,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public static void Mutate(IManagedMutator value) =>
                    value.Mutate();
            }
            """,
            "ManagedStructReceiverAnalyzerBoundary");

        const string sampleSource = """
            using SharpProof.Attributes;

            public static class Sample
            {
                [EnforcePure]
                public static void ByValue(ManagedValue value) =>
                    value.Mutate();

                [EnforcePure]
                public static void In(in ManagedValue value) =>
                    value.Mutate();

                [EnforcePure]
                public static void ReadonlyField(Owner owner) =>
                    owner.Value.Mutate();

                [EnforcePure]
                public static void ScalarIn(in ScalarValue value) =>
                    value.Mutate();

                [EnforcePure]
                public static void ScalarByValue(ScalarValue value) =>
                    value.Mutate();

                [EnforcePure]
                public static void BoxedArgument(ManagedValue value) =>
                    ExternalMutator.Mutate(value);
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(
            sampleSource,
            ["SP0002", "SP0047"],
            additionalReferences: [external]);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            "effects");

        AnalyzerTestHost.AssertIds(
            diagnostics,
            "SP0002",
            "SP0047",
            "SP0002",
            "SP0047",
            "SP0002");
    }
}
