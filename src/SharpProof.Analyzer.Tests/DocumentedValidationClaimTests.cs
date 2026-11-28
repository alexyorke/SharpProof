using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// SP0061 compares XML documentation validation sentences with the declared
// SharpProof contract on the same value. It reports only when both sides are
// readable, so each test pins either a consistent pair or one clear mismatch.
[TestFixture]
public sealed class DocumentedValidationClaimTests
{
    private const string Rule = "SP0061";

    [Test]
    public async Task DocumentedNonNullParameterWithNonNullAttributeIsConsistent()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="name">The name. Must not be null.</param>
                public static int Length([NonNull] string name) => name.Length;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedNonNullParameterWithoutDeclarationIsReported()
    {
        var diagnostics = await Analyze("""
            public static class Fixture {
                /// <param name="name">The name. Must not be null.</param>
                public static int Length(string name) => name.Length;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, [Rule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "Parameter 'name' is documented as 'Must not be null', but no NonNull attribute");
    }

    [Test]
    public async Task DocumentedNonNullParameterWithPrologueNullTestIsConsistent()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="name">Must not be null.</param>
                public static int Length(string name) {
                    Contract.Requires(name != null);
                    return name.Length;
                }
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedNonNullParameterWithPatternNullTestIsConsistent()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="name">Must not be null.</param>
                public static int Length(string name) {
                    Contract.Requires(name is not null);
                    return name.Length;
                }
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedRangeMatchedByInRangeIsConsistent()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="page">Page number. Must be between 1 and 100.</param>
                public static int Get([InRange(1, 100)] int page) => page;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedRangeWiderThanInRangeIsReported()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="page">Must be between 1 and 100.</param>
                public static int Get([InRange(1, 50)] int page) => page;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, [Rule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "documented as 'Must be between 1 and 100', but its declared contract admits 1 through 50");
    }

    [Test]
    public async Task DocumentedRangeMatchedByPrologueConjunctionIsConsistent()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="page">Must be between 1 and 100 inclusive.</param>
                public static int Get(int page) {
                    Contract.Requires(page >= 1 && page <= 100);
                    return page;
                }
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedRangeWithConstantOnTheLeftIsConsistent()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="page">Must be between 1 and 100.</param>
                public static int Get(int page) {
                    Contract.Requires(1 <= page && 100 >= page);
                    return page;
                }
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedRangeWithOnlyALowerPrologueBoundIsReported()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="page">Must be between 1 and 100.</param>
                public static int Get(int page) {
                    Contract.Requires(page >= 1);
                    return page;
                }
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, [Rule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "admits 1 through the type maximum");
    }

    [Test]
    public async Task DocumentedRangeWithoutAnyDeclarationIsReported()
    {
        var diagnostics = await Analyze("""
            public static class Fixture {
                /// <param name="page">Must be between 1 and 100.</param>
                public static int Get(int page) => page;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, [Rule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "no Positive, InRange attribute or Contract.Requires bounds it");
    }

    [Test]
    public async Task DocumentedPositiveMatchedByPositiveAttributeIsConsistent()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="count">Must be positive.</param>
                public static int Repeat([Positive] int count) => count;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedPositiveMatchedByPrologueIsConsistent()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="count">Must be greater than zero.</param>
                public static int Repeat(int count) {
                    Contract.Requires(count > 0);
                    return count;
                }
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedAtLeastAndAtMostAreMatchedByPrologue()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="value">Must be at least 2. Must be at most 9.</param>
                public static int Digit(int value) {
                    Contract.Requires(value >= 2);
                    Contract.Requires(value <= 9);
                    return value;
                }
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedRangeAgainstAnUnreadableClauseIsSilent()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="name">Must not be null.</param>
                public static int Length(string name) {
                    Contract.Requires(Check(name));
                    return name.Length;
                }
                private static bool Check(string value) => value is not null;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task UnrecognizedSentencesAreNotClaims()
    {
        var diagnostics = await Analyze("""
            public static class Fixture {
                /// <param name="name">Must not be null or empty.</param>
                /// <param name="page">Must be between 1 and 100 exclusive.</param>
                public static int Get(string name, int page) => page;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task RangeClaimsOnANonIntegralParameterAreNotCompared()
    {
        var diagnostics = await Analyze("""
            public static class Fixture {
                /// <param name="ratio">Must be between 0 and 1.</param>
                public static double Scale(double ratio) => ratio;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task AContradictoryDocumentedRangeIsNotCompared()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="page">Must be between 5 and 1.</param>
                public static int Get([InRange(1, 2)] int page) => page;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task ACompanionContractIsNotComparedWithTheTargetDocumentation()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <param name="page">Must be between 1 and 100.</param>
                public static int Get(int page) => page;
            }
            [ContractFor(typeof(Fixture))]
            public static class FixtureContracts {
                public static int Get(int page) {
                    Contract.Requires(page >= 1 && page <= 100);
                    return page;
                }
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task ARefParameterIsNotCompared()
    {
        var diagnostics = await Analyze("""
            public static class Fixture {
                /// <param name="count">Must be positive.</param>
                public static void Adjust(ref int count) => count++;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedNonNullReturnWithNonNullReturnAttributeIsConsistent()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <returns>The name. Must not be null.</returns>
                [return: NonNull]
                public static string Name() => "x";
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedNonNullReturnWithoutDeclarationIsReported()
    {
        var diagnostics = await Analyze("""
            public static class Fixture {
                /// <returns>Must not be null.</returns>
                public static string Name() => "x";
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, [Rule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "The return value of 'Name' is documented as 'Must not be null'");
    }

    [Test]
    public async Task DocumentedReturnLowerBoundMatchedByEnsuresIsConsistent()
    {
        var diagnostics = await Analyze("""
            using SharpProof.Attributes;
            public static class Fixture {
                /// <returns>Must be at least 0.</returns>
                public static int Square(int value) {
                    Contract.Ensures(Contract.Result<int>() >= 0);
                    return value * value;
                }
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, []);
    }

    [Test]
    public async Task DocumentedPositiveReturnWithoutDeclarationIsReported()
    {
        var diagnostics = await Analyze("""
            public static class Fixture {
                /// <returns>Must be positive.</returns>
                public static int Square(int value) => value * value;
            }
            """);
        AnalyzerTestHost.AssertIds(diagnostics, [Rule]);
        AnalyzerTestHost.AssertMessageContains(diagnostics[0],
            "no [return: Positive], [return: InRange] attribute or Contract.Ensures bounds it");
    }

    // The strict profile is the consumer default and always runs symbol
    // analysis. The advisory profile runs it only in files that use SharpProof
    // attributes, so these tests must not depend on that gate.
    private static Task<ImmutableArray<Diagnostic>> Analyze(string source)
    {
        return AnalyzerTestHost.AnalyzeAsync(source, "contracts", [Rule], profile: "strict");
    }
}
