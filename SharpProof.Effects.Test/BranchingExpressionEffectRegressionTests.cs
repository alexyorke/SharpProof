namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class BranchingExpressionEffectRegressionTests
{
    private static readonly Compilation TerminalInitializerCompilation =
        CreateTerminalInitializerCompilation();

    private static readonly Compilation InfeasibleInitializerCompilation =
        CreateInfeasibleInitializerCompilation();

    [TestCase("TerminalTrueInitializer")]
    [TestCase("TerminalFalseInitializer")]
    public void TerminalConditionalInitializerArmDoesNotSuppressReachableSiblingEffects(
        string typeName)
    {
        var compilation = TerminalInitializerCompilation;
        var summary = new EffectAnalysisSession(compilation)
            .Analyze(EffectTestHost.RequireType(compilation, typeName)
                .InstanceConstructors
                .Single(static constructor =>
                    !constructor.IsImplicitlyDeclared))
            .Summary;

        Assert.That(
            summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
    }

    private static CSharpCompilation CreateTerminalInitializerCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class TerminalTrueInitializer {
                private static bool s_condition;
                private static int s_state;

                private readonly int _value =
                    s_condition
                        ? throw new InvalidOperationException()
                        : s_state = 1;

                public TerminalTrueInitializer() {
                }
            }

            public sealed class TerminalFalseInitializer {
                private static bool s_condition;
                private static int s_state;

                private readonly int _value =
                    s_condition
                        ? s_state = 1
                        : throw new InvalidOperationException();

                public TerminalFalseInitializer() {
                }
            }
            """);
    }

    [TestCase("ShortCircuitAndInitializer")]
    [TestCase("ShortCircuitOrInitializer")]
    [TestCase("NonNullCoalesceInitializer")]
    public void InfeasibleInitializerBranchEffectsAreNotScanned(
        string typeName)
    {
        var compilation = InfeasibleInitializerCompilation;
        var summary = new EffectAnalysisSession(compilation)
            .Analyze(EffectTestHost.RequireType(compilation, typeName)
                .InstanceConstructors
                .Single(static constructor =>
                    !constructor.IsImplicitlyDeclared))
            .Summary;

        Assert.That(
            summary.Writes.Contains(EffectRegionId.Static()),
            Is.False,
            typeName);
    }

    private static CSharpCompilation CreateInfeasibleInitializerCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            public sealed class ShortCircuitAndInitializer {
                private static int s_state;
                private readonly bool _value = false && ++s_state > 0;

                public ShortCircuitAndInitializer() {
                }
            }

            public sealed class ShortCircuitOrInitializer {
                private static int s_state;
                private readonly bool _value = true || ++s_state > 0;

                public ShortCircuitOrInitializer() {
                }
            }

            public sealed class NonNullCoalesceInitializer {
                private static object? s_object;
                private readonly object _value =
                    new object() ?? (s_object = new object());

                public NonNullCoalesceInitializer() {
                }
            }
            """);
    }
}
