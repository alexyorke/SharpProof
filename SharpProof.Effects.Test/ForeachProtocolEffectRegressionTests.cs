namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ForeachProtocolEffectRegressionTests
{
    [Test]
    public void ForeachIncludesEveryProtocolPhase()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class EffectSink {
                private static int s_state;

                public static void Write() => s_state = 1;

                public static void Read() => _ = s_state;
            }

            public sealed class Element {
                public static implicit operator int(Element value) {
                    lock (value) {
                        return 0;
                    }
                }
            }

            public readonly struct Sequence {
                public Enumerator GetEnumerator() {
                    EffectSink.Write();
                    return default;
                }

                public ref struct Enumerator {
                    public bool MoveNext() {
                        EffectSink.Read();
                        return true;
                    }

                    public Element Current => new();

                    public void Dispose() =>
                        throw new ApplicationException();
                }
            }

            public static class Sample {
                public static void Run() {
                    foreach (int value in new Sequence()) {
                        _ = value;
                        break;
                    }
                }
            }
            """);
        var method = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "Run");

        var result = new EffectAnalysisSession(compilation).Analyze(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "GetEnumerator write");
            Assert.That(
                result.Summary.Reads.Contains(EffectRegionId.Static()),
                Is.True,
                "MoveNext read");
            Assert.That(
                result.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed),
                "Current allocation");
            Assert.That(
                result.Summary.Capabilities.Contains(
                    EffectCapabilityKind.Synchronization),
                Is.True,
                "element conversion synchronization");
            Assert.That(
                result.Summary.Throws.Types.Select(
                    static type => type.ToDisplayString()),
                Does.Contain("System.ApplicationException"),
                "Dispose exception");
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }
}
