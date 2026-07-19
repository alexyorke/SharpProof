using NUnit.Framework;
using SharpProof.Identity;
using SharpProof.Inference;

namespace SharpProof.Test;

[TestFixture]
public sealed class InferredMethodSummaryTests
{
    [Test]
    public void CacheKey_IsStableAndNormalizesEnvironmentalInputs()
    {
        var identity = CreateIdentity();

        var first = MethodSummaryCacheKey.Create(
            identity,
            " Assembly ",
            " BodyHash ",
            " Configuration ",
            " net8.0 ",
            InferredMethodSummary.SchemaVersion);
        var second = MethodSummaryCacheKey.Create(
            CreateIdentity(),
            "Assembly",
            "BodyHash",
            "Configuration",
            "net8.0",
            InferredMethodSummary.SchemaVersion);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.Method, Is.EqualTo(identity.ToCanonicalKey()));
    }

    [Test]
    public void UnknownSummary_RequiresExplicitReason()
    {
        Assert.Throws<ArgumentException>(() => new InferredMethodSummary(
            CreateIdentity(),
            InferredSummarySource.SymbolicBody,
            InferredPurity.Unknown,
            InferredMethodEffects.None,
            InferredFreshness.Unknown,
            InferredEffectVisibility.Unknown));
    }

    [Test]
    public void KnownSummary_RejectsUnknownReason()
    {
        Assert.Throws<ArgumentException>(() => new InferredMethodSummary(
            CreateIdentity(),
            InferredSummarySource.SymbolicBody,
            InferredPurity.Pure,
            InferredMethodEffects.None,
            InferredFreshness.None,
            InferredEffectVisibility.None,
            unknownReason: InferredSummaryUnknownReason.BudgetExhausted));
    }

    [Test]
    public void SemanticComparison_NormalizesEvidenceAndIgnoresSource()
    {
        var first = new InferredMethodSummary(
            CreateIdentity(),
            InferredSummarySource.SymbolicBody,
            InferredPurity.Impure,
            InferredMethodEffects.WritesStaticField | InferredMethodEffects.Throws,
            InferredFreshness.None,
            InferredEffectVisibility.CallerVisible,
            ["System.Exception", " System.InvalidOperationException ", "System.Exception"],
            ["B", "A"]);
        var second = new InferredMethodSummary(
            CreateIdentity(),
            InferredSummarySource.EffectSummary,
            InferredPurity.Impure,
            InferredMethodEffects.WritesStaticField | InferredMethodEffects.Throws,
            InferredFreshness.None,
            InferredEffectVisibility.CallerVisible,
            ["System.InvalidOperationException", "System.Exception"],
            ["A", "B"]);

        Assert.Multiple(() =>
        {
            Assert.That(first.Identity, Is.EqualTo(second.Identity));
            Assert.That(first.Purity, Is.EqualTo(second.Purity));
            Assert.That(first.Effects, Is.EqualTo(second.Effects));
            Assert.That(first.Freshness, Is.EqualTo(second.Freshness));
            Assert.That(first.EffectVisibility, Is.EqualTo(second.EffectVisibility));
            Assert.That(first.UnknownReason, Is.EqualTo(second.UnknownReason));
            Assert.That(first.ThrownExceptionTypes, Is.EqualTo(second.ThrownExceptionTypes));
            Assert.That(first.BlockingCallChain, Is.EqualTo(second.BlockingCallChain));
        });
    }

    [Test]
    public void EffectSummaryProjection_PreservesTypedSemantics()
    {
        var summary = InferredMethodSummary.FromEffectSummary(
            CreateIdentity(),
            "pure",
            ["allocates_array", "writes_indirect_memory", "throws"],
            "fresh_owned_array_write",
            "internal_only",
            ["System.InvalidOperationException", "System.Exception"],
            ["B", "A"],
            []);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Purity, Is.EqualTo(InferredPurity.Pure));
            Assert.That(summary.Freshness, Is.EqualTo(InferredFreshness.FreshOwnedArray));
            Assert.That(summary.EffectVisibility, Is.EqualTo(InferredEffectVisibility.InternalOnly));
            Assert.That(summary.Effects, Is.EqualTo(
                InferredMethodEffects.AllocatesArray |
                InferredMethodEffects.WritesIndirectMemory |
                InferredMethodEffects.Throws));
            Assert.That(summary.ThrownExceptionTypes,
                Is.EqualTo(new[] { "System.Exception", "System.InvalidOperationException" }));
        });
    }

    [Test]
    public void EffectSummaryProjection_UnresolvedDispatchRemainsUnknown()
    {
        var summary = InferredMethodSummary.FromEffectSummary(
            CreateIdentity(),
            "conservative_unknown",
            ["calls_method", "virtual_call"],
            "none",
            "caller_visible",
            [],
            ["Example.Interface.Call()"],
            ["dynamic_dispatch"]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Purity, Is.EqualTo(InferredPurity.Unknown));
            Assert.That(summary.UnknownReason, Is.EqualTo(InferredSummaryUnknownReason.UnresolvedDispatch));
            Assert.That(summary.Effects.HasFlag(InferredMethodEffects.DynamicDispatch), Is.True);
        });
    }

    private static StructuralMethodIdentity CreateIdentity()
    {
        return new StructuralMethodIdentity(
            "Example.Type",
            "method",
            "Compute",
            0,
            [new StructuralParameterIdentity("named:System.Int32", StructuralRefKinds.None)],
            "named:System.Int32",
            StructuralRefKinds.None);
    }
}
