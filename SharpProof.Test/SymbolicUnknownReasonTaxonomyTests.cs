using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SymbolicCapability = SharpProof.Attributes.SharpProofCapability;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicUnknownReasonTaxonomyTests
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestCase(SymbolicUnknownReason.UnsupportedIrEncoding, SymbolicUnknownReasonCategory.UnsupportedSyntax,
        "proof.unsupported_ir_encoding")]
    [TestCase(SymbolicUnknownReason.MethodBudgetExceeded, SymbolicUnknownReasonCategory.SolverBudget,
        "proof.solver_method_budget")]
    [TestCase(SymbolicUnknownReason.PathConditionBudgetExceeded, SymbolicUnknownReasonCategory.SolverBudget,
        "proof.solver_path_condition_budget")]
    [TestCase(SymbolicUnknownReason.ExpressionBudgetExceeded, SymbolicUnknownReasonCategory.SolverBudget,
        "proof.solver_expression_budget")]
    [TestCase(SymbolicUnknownReason.Timeout, SymbolicUnknownReasonCategory.SolverTimeout,
        "proof.solver_timeout")]
    [TestCase(SymbolicUnknownReason.CancellationRequested, SymbolicUnknownReasonCategory.Cancellation,
        "proof.canceled")]
    [TestCase(SymbolicUnknownReason.SmtUnavailable, SymbolicUnknownReasonCategory.NativeSolverFailure,
        "proof.native_solver_failure")]
    public void ProofTaxonomy_MapsStableCodesAndCategories(
        SymbolicUnknownReason reason,
        SymbolicUnknownReasonCategory category,
        string code)
    {
        var info = SymbolicUnknownReasonTaxonomy.ForProof(reason, "raw_reason");

        Assert.Multiple(() =>
        {
            Assert.That(info.Source, Is.EqualTo(SymbolicUnknownReasonSource.Proof));
            Assert.That(info.Category, Is.EqualTo(category));
            Assert.That(info.Code, Is.EqualTo(code));
            Assert.That(info.RawReason, Is.EqualTo("raw_reason"));
            Assert.That(info.IsUnknown, Is.True);
        });
    }

    [Test]
    public void FamilyTaxonomies_DistinguishSyntaxLibraryDispatchExternalRecursionAndCancellation()
    {
        var capabilityLibrary = SymbolicUnknownReasonTaxonomy.ForCapability(
            SymbolicCapabilityUnknownReason.MetadataClassificationUnavailable);
        var capabilityDispatch = SymbolicUnknownReasonTaxonomy.ForCapability(
            SymbolicCapabilityUnknownReason.DynamicDispatch);
        var complexitySyntax = SymbolicUnknownReasonTaxonomy.ForComplexity(
            SymbolicComplexityUnknownReason.UnsupportedLoopShape);
        var complexityExternal = SymbolicUnknownReasonTaxonomy.ForComplexity(
            SymbolicComplexityUnknownReason.ExternalCallee);
        var complexityRecursion = SymbolicUnknownReasonTaxonomy.ForComplexity(
            SymbolicComplexityUnknownReason.RecursiveCycle);
        var complexityCancellation = SymbolicUnknownReasonTaxonomy.ForComplexity(
            SymbolicComplexityUnknownReason.CancellationRequested);

        Assert.Multiple(() =>
        {
            Assert.That(capabilityLibrary.Category, Is.EqualTo(SymbolicUnknownReasonCategory.UnsupportedLibraryModel));
            Assert.That(capabilityDispatch.Category, Is.EqualTo(SymbolicUnknownReasonCategory.DynamicDispatch));
            Assert.That(complexitySyntax.Category, Is.EqualTo(SymbolicUnknownReasonCategory.UnsupportedSyntax));
            Assert.That(complexityExternal.Category, Is.EqualTo(SymbolicUnknownReasonCategory.ExternalBoundary));
            Assert.That(complexityRecursion.Category, Is.EqualTo(SymbolicUnknownReasonCategory.RecursiveAnalysis));
            Assert.That(complexityCancellation.Category, Is.EqualTo(SymbolicUnknownReasonCategory.Cancellation));
            Assert.That(complexityCancellation.IsRetryable, Is.True);
        });
    }

    [Test]
    public void RuntimeEnsuresAndPurityTaxonomies_PreserveFamilyAndRawReason()
    {
        var runtime = SymbolicUnknownReasonTaxonomy.ForRuntimeHazard(
            SymbolicRuntimeHazardStatus.Unsupported,
            "unsupported_typed_projection",
            SymbolicUnknownReason.UnsupportedIrEncoding);
        var ensures = SymbolicUnknownReasonTaxonomy.ForEnsures("condition_parse_failure");
        var ensuresBudget = SymbolicUnknownReasonTaxonomy.ForEnsures(
            "smt_method_budget_exceeded",
            SymbolicUnknownReason.MethodBudgetExceeded);
        var purity = SymbolicUnknownReasonTaxonomy.ForPurity(
            "unknown_metadata_call",
            "no definitive generated summary");

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Source, Is.EqualTo(SymbolicUnknownReasonSource.RuntimeHazard));
            Assert.That(runtime.Code, Is.EqualTo("runtime_hazard.unsupported_typed_projection"));
            Assert.That(runtime.Category, Is.EqualTo(SymbolicUnknownReasonCategory.UnsupportedOperation));
            Assert.That(ensures.Source, Is.EqualTo(SymbolicUnknownReasonSource.Ensures));
            Assert.That(ensures.Code, Is.EqualTo("ensures.invalid_condition"));
            Assert.That(ensures.Category, Is.EqualTo(SymbolicUnknownReasonCategory.InvalidInput));
            Assert.That(ensuresBudget.Code, Is.EqualTo("ensures.solver_method_budget"));
            Assert.That(purity.Source, Is.EqualTo(SymbolicUnknownReasonSource.Purity));
            Assert.That(purity.Category, Is.EqualTo(SymbolicUnknownReasonCategory.UnsupportedLibraryModel));
            Assert.That(purity.RawReason, Is.EqualTo("no definitive generated summary"));
        });
    }

    [Test]
    public void PublicAndCompactResults_ExposeAdditiveUnknownReasonDetails()
    {
        var capability = new SymbolicCapabilityResult(
            "Example.cs",
            "M",
            "C.M()",
            "Method",
            0,
            1,
            1,
            1,
            1,
            2,
            SymbolicCapability.None,
            "None",
            unknownReasons: new[] { SymbolicCapabilityUnknownReason.DynamicDispatch });
        var complexity = new SymbolicComplexityResult(
            "Example.cs",
            "M",
            "C.M()",
            "Method",
            0,
            1,
            1,
            1,
            1,
            2,
            new SymbolicComplexityInfo("unknown", SymbolicComplexityKind.Unknown, true, true, false),
            unknownReasons: new[] { SymbolicComplexityUnknownReason.UnknownCallee });
        var proof = new SymbolicProofInfo(
            SymbolicProofStatus.Unknown,
            SymbolicProofBackend.Smt,
            SymbolicUnknownReason.Timeout,
            "smt_timeout",
            false,
            null);

        Assert.Multiple(() =>
        {
            Assert.That(capability.UnknownReasonDetails.Single().Code,
                Is.EqualTo("capability.dynamic_dispatch"));
            Assert.That(JsonSerializer.SerializeToElement(capability, CanonicalJsonOptions)
                    .GetProperty("unknownReasonDetails")[0]
                    .GetProperty("category").GetString(),
                Is.EqualTo(SymbolicUnknownReasonCategory.DynamicDispatch.ToString()));
            Assert.That(complexity.UnknownReasonDetails.Single().Code,
                Is.EqualTo("complexity.unknown_callee"));
            Assert.That(JsonSerializer.SerializeToElement(complexity, CanonicalJsonOptions)
                    .GetProperty("unknownReasonDetails")[0]
                    .GetProperty("category").GetString(),
                Is.EqualTo(SymbolicUnknownReasonCategory.UnsupportedLibraryModel.ToString()));
            Assert.That(proof.UnknownReasonInfo.Code, Is.EqualTo("proof.solver_timeout"));
            Assert.That(proof.UnknownReasonInfo.IsRetryable, Is.True);
        });
    }

    [Test]
    public void PurityEvidence_EmitsStableUnknownDiagnosticProperties()
    {
        var evidence = PurityAnalysisEngine.PurityEvidence.Create("unsupported_operation");

        var properties = evidence.ToDiagnosticProperties();

        Assert.Multiple(() =>
        {
            Assert.That(evidence.UnknownReasonInfo.Code, Is.EqualTo("purity.unsupported_operation"));
            Assert.That(properties[SharpProofDiagnostics.UnknownReasonCodeProperty],
                Is.EqualTo("purity.unsupported_operation"));
            Assert.That(properties[SharpProofDiagnostics.UnknownReasonCategoryProperty],
                Is.EqualTo(SymbolicUnknownReasonCategory.UnsupportedOperation.ToString()));
            Assert.That(properties[SharpProofDiagnostics.UnknownReasonSourceProperty],
                Is.EqualTo(SymbolicUnknownReasonSource.Purity.ToString()));
        });
    }

    [Test]
    public void AnalyzerQueryFailures_UseRetryableAnalysisUnavailableTaxonomy()
    {
        var capability = SymbolicUnknownReasonTaxonomy.ForCapabilityFailure("SPQ9000: failed");
        var purity = PurityAnalysisEngine.PurityEvidence.Create("analysis_failure").UnknownReasonInfo;

        Assert.Multiple(() =>
        {
            Assert.That(capability.Code, Is.EqualTo("capability.analysis_failure"));
            Assert.That(capability.Category, Is.EqualTo(SymbolicUnknownReasonCategory.AnalysisUnavailable));
            Assert.That(capability.IsRetryable, Is.True);
            Assert.That(purity.Code, Is.EqualTo("purity.analysis_failure"));
            Assert.That(purity.Category, Is.EqualTo(SymbolicUnknownReasonCategory.AnalysisUnavailable));
            Assert.That(purity.IsRetryable, Is.True);
        });
    }
}
