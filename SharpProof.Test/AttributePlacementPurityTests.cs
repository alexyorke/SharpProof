using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class AttributePlacementPurityTests
{
    [Test]
    public async Task PureAttributeOnProperty_NoPlacementDiagnostic()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Pure]
    public int Value => 42;

    [EnforcePure]
    public int TestMethod()
    {
        return Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureAttributeOnIndexer_NoPlacementDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [Pure]
    public int this[int value] => value;

    [EnforcePure]
    public int TestMethod(int value)
    {
        return this[value];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnforcePureAttributeOnProperty_AliasesGetterWithoutPlacementDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int Value => 42;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task MultipleGetterAliasAttributesOnProperty_NoPlacementDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure, ZeroAllocations]
    public int Value => 42;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StackedMisplacedAttributes_ReportEveryOccurrence()
    {
        const string source = @"
using System;
using SharpProof.Attributes;

[EnforcePure,
 Pure,
 AllowSynchronization,
 ZeroAllocations,
 AllowedCapabilities(SharpProofCapability.None),
 Ensures(""result != null""),
 Ensures(""result.Length > 0""),
 Requires(""true""),
 Requires(""false""),
 DoesNotThrow,
 AllowedExceptions(typeof(Exception)),
 AllowedExceptions(typeof(InvalidOperationException)),
 ExpectedComplexity(ComplexityKind.Constant)]
public sealed class TestClass
{
}
";

        var placementIds = new[]
        {
            SharpProofDiagnostics.MisplacedAttributeId,
            SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeId,
            SharpProofDiagnostics.MisplacedZeroAllocationsAttributeId,
            SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeId,
            SharpProofDiagnostics.MisplacedEnsuresAttributeId,
            SharpProofDiagnostics.MisplacedRequiresAttributeId,
            SharpProofDiagnostics.MisplacedExceptionContractAttributeId,
            SharpProofDiagnostics.MisplacedExpectedComplexityAttributeId
        };
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source, concurrentAnalysis: false);
        var misplaced = diagnostics.Where(diagnostic => placementIds.Contains(diagnostic.Id)).ToArray();

        Assert.That(misplaced.Select(diagnostic => diagnostic.Id), Is.EquivalentTo(new[]
        {
            SharpProofDiagnostics.MisplacedAttributeId,
            SharpProofDiagnostics.MisplacedAttributeId,
            SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeId,
            SharpProofDiagnostics.MisplacedZeroAllocationsAttributeId,
            SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeId,
            SharpProofDiagnostics.MisplacedEnsuresAttributeId,
            SharpProofDiagnostics.MisplacedEnsuresAttributeId,
            SharpProofDiagnostics.MisplacedRequiresAttributeId,
            SharpProofDiagnostics.MisplacedRequiresAttributeId,
            SharpProofDiagnostics.MisplacedExceptionContractAttributeId,
            SharpProofDiagnostics.MisplacedExceptionContractAttributeId,
            SharpProofDiagnostics.MisplacedExceptionContractAttributeId,
            SharpProofDiagnostics.MisplacedExpectedComplexityAttributeId
        }));
        Assert.That(
            misplaced.Select(diagnostic => diagnostic.Location.SourceTree!
                .GetText()
                .ToString(diagnostic.Location.SourceSpan)),
            Is.EquivalentTo(new[]
            {
                "EnforcePure",
                "Pure",
                "AllowSynchronization",
                "ZeroAllocations",
                "AllowedCapabilities(SharpProofCapability.None)",
                "Ensures(\"result != null\")",
                "Ensures(\"result.Length > 0\")",
                "Requires(\"true\")",
                "Requires(\"false\")",
                "DoesNotThrow",
                "AllowedExceptions(typeof(Exception))",
                "AllowedExceptions(typeof(InvalidOperationException))",
                "ExpectedComplexity(ComplexityKind.Constant)"
            }));
    }

    [Test]
    public async Task EnforcePureAttributeOnConversionOperator_NoPlacementDiagnostic()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public readonly struct Temperature
{
    private readonly int _celsius;

    public Temperature(int celsius)
    {
        _celsius = celsius;
    }

    [EnforcePure]
    public static explicit operator int(Temperature value)
    {
        return value._celsius;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
