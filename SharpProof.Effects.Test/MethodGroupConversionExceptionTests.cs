namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class MethodGroupConversionExceptionTests
{
    [Test]
    public void NullClosedDelegateUsesTheDispatchSpecificExceptionOrder()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public class Target {
                public void NonVirtual() {
                }

                public virtual void Virtual() {
                }
            }

            public static class Sample {
                private static int s_state;

                public static void NonVirtualCatch() {
                    Target target = null!;
                    try {
                        Action action = target.NonVirtual;
                    }
                    catch (ArgumentException) {
                        s_state++;
                    }
                }

                public static void NonVirtualWrongCatch() {
                    Target target = null!;
                    try {
                        Action action = target.NonVirtual;
                    }
                    catch (NullReferenceException) {
                        s_state++;
                    }
                }

                public static void VirtualCatch() {
                    Target target = null!;
                    try {
                        Action action = target.Virtual;
                    }
                    catch (NullReferenceException) {
                        s_state++;
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var nonVirtual = Analyze("NonVirtualCatch");
        var wrongCatch = Analyze("NonVirtualWrongCatch");
        var virtualCall = Analyze("VirtualCatch");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                nonVirtual.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "ArgumentException reaches the matching catch");
            Assert.That(
                nonVirtual.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed),
                "delegate allocation precedes the nonvirtual target check");
            Assert.That(nonVirtual.Throws.IsEmpty, Is.True);

            Assert.That(
                wrongCatch.Writes.Contains(EffectRegionId.Static()),
                Is.False,
                "NullReferenceException cannot catch the constructor failure");
            Assert.That(
                ExceptionNames(wrongCatch),
                Is.EqualTo(["System.ArgumentException"]));
            Assert.That(
                wrongCatch.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));

            Assert.That(
                virtualCall.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "virtual dispatch still throws NullReferenceException");
            Assert.That(
                virtualCall.Allocation,
                Is.EqualTo(EffectAllocationKind.None),
                "virtual dispatch fails before delegate allocation");
            Assert.That(virtualCall.Throws.IsEmpty, Is.True);
        }

        EffectSummary Analyze(string methodName)
        {
            return session.Analyze(EffectTestHost.RequireMethod(
                    compilation,
                    "Sample",
                    methodName))
                .Summary;
        }
    }

    private static string[] ExceptionNames(EffectSummary summary)
    {
        return [.. summary.Throws.Types
            .Select(static type => type.ToDisplayString())
            .OrderBy(static name => name, StringComparer.Ordinal)];
    }
}
