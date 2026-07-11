using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class PropertyContractAliasTests
{
    [Test]
    public async Task GetterBearingPropertyAndIndexer_AcceptGetterContractAliases()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class AliasTarget
            {
                [EnforcePure]
                [ZeroAllocations]
                [AllowedCapabilities(SharpProofCapability.None)]
                [Ensures("result == 1")]
                [ExpectedComplexity(ComplexityKind.Constant)]
                [DoesNotThrow]
                [AllowedExceptions(typeof(Exception))]
                public int Value => 1;

                [EnforcePure]
                [ZeroAllocations]
                [AllowedCapabilities(SharpProofCapability.None)]
                [Ensures("result == index")]
                [ExpectedComplexity(ComplexityKind.Constant)]
                [DoesNotThrow]
                [AllowedExceptions(typeof(Exception))]
                public int this[int index] => index;
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task GetterAliases_EnforceEverySupportedContractFamily()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class AliasTarget
            {
                [EnforcePure]
                public int Purity
                {
                    get
                    {
                        Console.WriteLine(1);
                        return 1;
                    }
                }

                [ZeroAllocations]
                public object Allocation => new object();

                [AllowedCapabilities(SharpProofCapability.None)]
                public int Capability
                {
                    get
                    {
                        Console.WriteLine(1);
                        return 1;
                    }
                }

                [Ensures("result > 0")]
                public int Postcondition => 0;

                [ExpectedComplexity(ComplexityKind.Linear)]
                public int this[int n]
                {
                    get
                    {
                        var sum = 0;
                        for (var i = 0; i < n; i++)
                        for (var j = 0; j < n; j++)
                            sum += i + j;
                        return sum;
                    }
                }

                [DoesNotThrow]
                public int Exception => throw new InvalidOperationException();
            }
            """);

        var expectedIds = new[]
        {
            SharpProofDiagnostics.PurityNotVerifiedId,
            SharpProofDiagnostics.AllocationInZeroAllocationMethodId,
            SharpProofDiagnostics.CapabilityViolationId,
            SharpProofDiagnostics.EnsuresNotProvenId,
            SharpProofDiagnostics.ComplexityExceededId,
            SharpProofDiagnostics.ExceptionContractViolationId
        };
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Is.EquivalentTo(expectedIds));
        foreach (var diagnosticId in expectedIds)
            Assert.That(diagnostics.Count(diagnostic => diagnostic.Id == diagnosticId), Is.EqualTo(1), diagnosticId);
    }

    [Test]
    public async Task PropertyContractAliases_ApplyOnlyToGetter()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class AliasTarget
            {
                [EnforcePure]
                public int Value
                {
                    get => 1;
                    set => Console.WriteLine(value);
                }

                [ZeroAllocations]
                public object Item
                {
                    get => new object();
                    set { _ = new object(); }
                }
            }
            """);

        Assert.That(diagnostics.Count(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.Zero);
        Assert.That(
            diagnostics.Count(diagnostic =>
                diagnostic.Id == SharpProofDiagnostics.AllocationInZeroAllocationMethodId),
            Is.EqualTo(1));
    }

    [Test]
    public async Task SetterOnlyProperty_RemainsInvalidGetterAliasTarget()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public sealed class AliasTarget
            {
                [EnforcePure]
                [ZeroAllocations]
                public int Value
                {
                    set { }
                }
            }
            """);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(new[]
            {
                SharpProofDiagnostics.MisplacedAttributeId,
                SharpProofDiagnostics.MisplacedZeroAllocationsAttributeId
            }));
    }

    [Test]
    public async Task AutoPropertyGetter_UsesExactEffectAliasesAndConservativeEnsures()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public sealed class AliasTarget
            {
                [EnforcePure]
                [ZeroAllocations]
                [AllowedCapabilities(SharpProofCapability.None)]
                [ExpectedComplexity(ComplexityKind.Constant)]
                [DoesNotThrow]
                public int ExactEffects { get; } = 1;

                [Ensures("result > 0")]
                public int UnknownResult { get; } = 1;
            }
            """);

        var diagnostic = diagnostics.Single();
        Assert.That(diagnostic.Id, Is.EqualTo(SharpProofDiagnostics.EnsuresUnsupportedId));
        Assert.That(diagnostic.GetMessage(), Does.Contain("auto-property getter result is not source-visible"));
    }

    private static Task<ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> GetDiagnosticsAsync(string source)
    {
        return AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            globalOptions: ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_suggest_missing_enforce_pure", "false"),
            concurrentAnalysis: true,
            compilationName: "PropertyContractAliases");
    }
}
