using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class ApiSpecContentDigestTests
{
    [Test]
    public void ValidatedTraversalPreservesPublishedDefaultCatalogDigest()
    {
        // Version 9 removes unconditional resource-constructor callback guarantees.
        // Internal representation changes must preserve this semantic identity.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiSpecTable.DefaultTableVersion, Is.EqualTo("9"));
            Assert.That(ApiSpecTable.Default.ContentSha256, Is.EqualTo(
                "e2b952334c6c2a3f6d1a9b04954bc1d8515a25e6d5f2d031f7ffedfc3bbe0adb"));
        }
    }

    [Test]
    public void MayThrowExceptionMetadataIsHashedAsASet()
    {
        var canonical = CreateTable([
            "System.ArgumentException",
            "System.InvalidOperationException"
        ]);
        var reorderedWithDuplicates = CreateTable([
            "System.InvalidOperationException",
            "System.ArgumentException",
            "System.InvalidOperationException"
        ]);
        var different = CreateTable([
            "System.InvalidOperationException"
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                reorderedWithDuplicates.ContentSha256,
                Is.EqualTo(canonical.ContentSha256));
            Assert.That(
                different.ContentSha256,
                Is.Not.EqualTo(canonical.ContentSha256));
        }
    }

    [Test]
    public void NormalCompletionConditionIsHashed()
    {
        var evidence = new SpecEvidence(
            SpecEvidenceKind.Documented,
            "normal-completion-test");
        var target = new ApiSpecTarget(
            "normal-completion-test",
            "M:Missing.NormalCompletion.Run",
            "Missing.NormalCompletion",
            SpecTargetMemberKind.Method,
            "Run",
            true,
            0,
            null,
            [],
            IrTypeKind.Integer,
            [new ApiSpecAssemblyIdentity("Missing", string.Empty)]);
        ApiSpecTable Create(SpecTermDeclaration completion)
        {
            System.ArgumentNullException.ThrowIfNull(completion);
            return ApiSpecTable.Create([
                new ApiSpecDeclaration(
                    target,
                    new ApiSpecFacets(
                        new SpecEffectFacet(SpecEffect.None, evidence),
                        new SpecAllocationFacet(
                            SpecAllocationBehavior.None,
                            evidence),
                        new SpecThrowFacet(
                            SpecThrowBehavior.MayThrow,
                            ["System.Exception"],
                            evidence,
                            completion),
                        new SpecNullnessFacet(
                            SpecNullness.NotApplicable,
                            evidence),
                        new SpecCardinalityFacet(
                            SpecCardinality.NotApplicable,
                            null,
                            evidence)),
                    [])
            ]);
        }
        var alwaysComplete = Create(new SpecBooleanDeclaration(true));
        var sometimesComplete = Create(new SpecBooleanDeclaration(false));

        Assert.That(
            alwaysComplete.ContentSha256,
            Is.Not.EqualTo(sometimesComplete.ContentSha256));
    }

    private static ApiSpecTable CreateTable(
        ImmutableArray<string> exceptionMetadataNames)
    {

        if (exceptionMetadataNames.IsDefault)
        {
            throw new System.ArgumentException("Array must be initialized.", nameof(exceptionMetadataNames));
        }
        var evidence = new SpecEvidence(
            SpecEvidenceKind.Documented,
            "exception-set-test");
        return ApiSpecTable.Create([
            new ApiSpecDeclaration(
                new ApiSpecTarget(
                    "exception-set-test",
                    "M:Missing.ExceptionSet.Run",
                    "Missing.ExceptionSet",
                    SpecTargetMemberKind.Method,
                    "Run",
                    true,
                    0,
                    null,
                    [],
                    IrTypeKind.Integer,
                    [new ApiSpecAssemblyIdentity("Missing", string.Empty)]),
                new ApiSpecFacets(
                    new SpecEffectFacet(SpecEffect.None, evidence),
                    new SpecAllocationFacet(
                        SpecAllocationBehavior.None,
                        evidence),
                    new SpecThrowFacet(
                        SpecThrowBehavior.MayThrow,
                        exceptionMetadataNames,
                        evidence),
                    new SpecNullnessFacet(
                        SpecNullness.NotApplicable,
                        evidence),
                    new SpecCardinalityFacet(
                        SpecCardinality.NotApplicable,
                        null,
                        evidence)),
                [])
        ]);
    }
}
