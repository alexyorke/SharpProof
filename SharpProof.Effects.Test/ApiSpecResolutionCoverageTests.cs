using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ApiSpecResolutionCoverageTests
{
    [Test]
    public void ResolutionRejectsIncompatibleParameterAndResultKinds()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int Return(int value) => value;
            }
            """);

        var parameterMismatch = Resolve(
            compilation,
            parameterType: IrTypeKind.String,
            resultType: IrTypeKind.Integer);
        var resultMismatch = Resolve(
            compilation,
            parameterType: IrTypeKind.Integer,
            resultType: IrTypeKind.Boolean);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                parameterMismatch.Failures.Single().Kind,
                Is.EqualTo(ApiSpecResolutionFailureKind.IncompatibleMemberShape));
            Assert.That(
                resultMismatch.Failures.Single().Kind,
                Is.EqualTo(ApiSpecResolutionFailureKind.IncompatibleMemberShape));
        }
    }

    [Test]
    public void ResolutionRequiresTheDeclaredReceiverKind()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample {
                public int Return(int value) => value;
            }
            """);
        var table = CreateTable(
            isStatic: false,
            receiverType: IrTypeKind.String,
            parameterType: IrTypeKind.Integer,
            resultType: IrTypeKind.Integer);

        var resolved = new ApiSpecResolver(table).Resolve(compilation);

        Assert.That(
            resolved.Failures.Single().Kind,
            Is.EqualTo(ApiSpecResolutionFailureKind.IncompatibleMemberShape));
    }

    [Test]
    public void ResolutionAcceptsMatchingDeclaredKinds()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int Return(int value) => value;
            }
            """);
        var resolved = new ApiSpecResolver(
            CreateTable(
                isStatic: true,
                receiverType: null,
                parameterType: IrTypeKind.Integer,
                resultType: IrTypeKind.Integer))
            .Resolve(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.IsComplete, Is.True);
            Assert.That(resolved.Specs, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public void ResolutionAcceptsCallerOwnedGenericTypeParameters()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static T Identity<T>(T value) => value;
            }
            """);
        var evidence = new SpecEvidence(
            SpecEvidenceKind.Observed,
            "generic-resolution-test");
        var resolved = new ApiSpecResolver(
            ApiSpecTable.Create([
                new ApiSpecDeclaration(
                    new ApiSpecTarget(
                        "resolution.generic",
                        "M:Sample.Identity``1(``0)",
                        "Sample",
                        SpecTargetMemberKind.Method,
                        "Identity",
                        true,
                        1,
                        null,
                        [IrTypeKind.Reference],
                        IrTypeKind.Reference,
                        [new ApiSpecAssemblyIdentity(
                            "EffectsTest",
                            string.Empty)]),
                    new ApiSpecFacets(
                        new SpecEffectFacet(SpecEffect.None, evidence),
                        new SpecAllocationFacet(
                            SpecAllocationBehavior.None,
                            evidence),
                        new SpecThrowFacet(
                            SpecThrowBehavior.DoesNotThrow,
                            [],
                            evidence),
                        new SpecNullnessFacet(
                            SpecNullness.NotApplicable,
                            evidence),
                        new SpecCardinalityFacet(
                            SpecCardinality.NotApplicable,
                            null,
                            evidence)),
                    [])]))
            .Resolve(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.IsComplete, Is.True);
            Assert.That(resolved.Specs, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public void NuGetRestoredFrameworkReferencePackIsApproved()
    {
        Assert.That(
            EffectContractMappingCatalog.ReferenceFamilyMarkers,
            Does.Contain((
                "/MICROSOFT.NETCORE.APP.REF/",
                ApiSpecReferenceFamily.MicrosoftNetCoreReferencePack)));
    }

    [Test]
    public void ReferenceFamilyWithoutAssemblyMetadataFailsClosed()
    {
        Assert.That(
            ApiSpecResolver.HasExpectedReferenceMetadata(
                null,
                ApiSpecReferenceFamily.MicrosoftNetCoreReferencePack),
            Is.False);
    }

    private static ResolvedApiSpecTable Resolve(
        Compilation compilation,
        IrTypeKind parameterType,
        IrTypeKind resultType)
    {
        return new ApiSpecResolver(
                CreateTable(
                    isStatic: true,
                    receiverType: null,
                    parameterType: parameterType,
                    resultType: resultType))
            .Resolve(compilation);
    }

    private static ApiSpecTable CreateTable(
        bool isStatic,
        IrTypeKind? receiverType,
        IrTypeKind parameterType,
        IrTypeKind resultType)
    {
        var evidence = new SpecEvidence(
            SpecEvidenceKind.Observed,
            "resolution-test");
        return ApiSpecTable.Create([
            new ApiSpecDeclaration(
                new ApiSpecTarget(
                    "resolution.test",
                    "M:Sample.Return(System.Int32)",
                    "Sample",
                    SpecTargetMemberKind.Method,
                    "Return",
                    isStatic,
                    0,
                    receiverType,
                    [parameterType],
                    resultType,
                    [new ApiSpecAssemblyIdentity(
                        "EffectsTest",
                        string.Empty)]),
                new ApiSpecFacets(
                    new SpecEffectFacet(SpecEffect.None, evidence),
                    new SpecAllocationFacet(
                        SpecAllocationBehavior.None,
                        evidence),
                    new SpecThrowFacet(
                        SpecThrowBehavior.DoesNotThrow,
                        [],
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
