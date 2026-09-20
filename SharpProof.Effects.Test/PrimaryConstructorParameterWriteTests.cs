namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class PrimaryConstructorParameterWriteTests
{
    [Test]
    public void CapturedPrimaryConstructorIntegerIsNotProvenNonZeroAfterInstanceCall()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample(int count) {
                public int Divide() {
                    if (count != 0) {
                        Clear();
                        return 10 / count;
                    }

                    return 0;
                }

                private void Clear() {
                    count = 0;
                }
            }
            """);

        var result = new EffectAnalysisSession(compilation)
            .Analyze(EffectTestHost.RequireMethod(compilation, "Sample", "Divide"));

        Assert.That(
            result.Summary.Throws.Types.Select(static type => type.MetadataName),
            Does.Contain("DivideByZeroException"),
            result.Summary.ToString());
    }

    [Test]
    public void CapturedPrimaryConstructorReferenceIsNotProvenNonNullAfterInstanceCall()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample(string? text) {
                public int Length() {
                    if (text != null) {
                        Clear();
                        return text.Length;
                    }

                    return 0;
                }

                private void Clear() {
                    text = null;
                }
            }
            """);

        var result = new EffectAnalysisSession(compilation)
            .Analyze(EffectTestHost.RequireMethod(compilation, "Sample", "Length"));

        Assert.That(
            result.Summary.Throws.Types.Select(static type => type.MetadataName),
            Does.Contain("NullReferenceException"));
    }

    [Test]
    public void AssignmentWritesReceiverBackedPrimaryConstructorStorage()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample(int state) {
                public void Assign(int value) {
                    state = value;
                }
            }
            """);
        var method = EffectTestHost.SampleMethod(compilation, "Assign");

        var result = new EffectAnalysisSession(compilation)
            .Analyze(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Regions,
                Is.EqualTo(new[] { EffectRegionId.Receiver }));
            Assert.That(result.Summary.Reads.IsEmpty, Is.True);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                result.Projection.Effects,
                Is.EqualTo(SharpProofEffect.WritesReceiverState));
            Assert.That(result.Projection.IsComplete, Is.True);
        }
    }
}
