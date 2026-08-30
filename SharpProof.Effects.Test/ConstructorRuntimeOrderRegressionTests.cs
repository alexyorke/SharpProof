namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ConstructorRuntimeOrderRegressionTests
{
    [Test]
    public void DelegatedBaseEffectsPrecedeFailingMemberInitializer()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public class Base {
                private int state;

                protected Base() {
                    state++;
                }
            }

            public sealed class Derived : Base {
                private static int marker;
                private readonly int value = Fail();

                public Derived() : this(MarkDelegation()) { }

                private Derived(int ignored) : base() { }

                private static int MarkDelegation() {
                    marker++;
                    return 0;
                }

                private static int Fail() =>
                    throw new InvalidOperationException();
            }
            """);
        var constructor = EffectTestHost.RequireType(compilation, "Derived")
            .InstanceConstructors
            .Single(static candidate =>
                candidate.DeclaredAccessibility == Accessibility.Public);

        var summary = new EffectAnalysisSession(compilation)
            .Analyze(constructor)
            .Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "the delegating initializer argument runs first");
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Receiver),
                Is.True,
                "the base constructor runs before derived initializers");
            Assert.That(
                summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(summary.Throws.IncludesUnknown, Is.False);
            Assert.That(
                summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.InvalidOperationException"));
        }
    }
}
