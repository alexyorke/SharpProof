using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ExceptionIdentityReplayTests
{
    [Test]
    public void AliasedExceptionIdentitiesRemainDistinctWhileFrameworkThrowsReplay()
    {
        var allowedReference = CreateExceptionReference(
            "Collision.Exceptions",
            new Version(1, 0, 0, 0),
            "allowed");
        var thrownReference = CreateExceptionReference(
            "Collision.Exceptions",
            new Version(2, 0, 0, 0),
            "thrown");
        var tree = CSharpSyntaxTree.ParseText(
            """
            extern alias allowed;
            extern alias thrown;
            using SharpProof.Attributes;

            public static class Subject {
                public static void Compare(
                    allowed::Collision.BoomException first,
                    thrown::Collision.BoomException second) {
                }

                public static void CompareConstructed(
                    allowed::Collision.GenericBoomException<int> first,
                    allowed::Collision.GenericBoomException<string> second) {
                }

                [AllowedExceptions(
                    typeof(allowed::Collision.BoomException))]
                public static void ThrowAliased() =>
                    throw new thrown::Collision.BoomException();

                [AllowedExceptions(typeof(System.ArgumentException))]
                public static void ThrowFramework() =>
                    throw new System.InvalidOperationException();
            }
            """,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            path: "ExceptionIdentityReplay.cs");
        var compilation = CSharpCompilation.Create(
            "Collision.Consumer",
            [tree],
            PlatformReferences
                .Add(AttributeReference)
                .Add(allowedReference)
                .Add(thrownReference),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic => diagnostic.ToString())));

        var method = compilation.GetTypeByMetadataName("Subject")!
            .GetMembers("Compare")
            .OfType<IMethodSymbol>()
            .Single();
        var allowed = (INamedTypeSymbol)method.Parameters[0].Type;
        var thrown = (INamedTypeSymbol)method.Parameters[1].Type;
        var allowedDocumentationId =
            DocumentationCommentId.CreateDeclarationId(allowed)!;
        var thrownDocumentationId =
            DocumentationCommentId.CreateDeclarationId(thrown)!;
        var allowedIdentity = CompilerExceptionTypeIdentity.Encode(allowed);
        var thrownIdentity = CompilerExceptionTypeIdentity.Encode(thrown);
        var constructedMethod = compilation.GetTypeByMetadataName("Subject")!
            .GetMembers("CompareConstructed")
            .OfType<IMethodSymbol>()
            .Single();
        var constructedInteger =
            (INamedTypeSymbol)constructedMethod.Parameters[0].Type;
        var constructedString =
            (INamedTypeSymbol)constructedMethod.Parameters[1].Type;
        var allowedReferenceId =
            DocumentationCommentId.CreateReferenceId(allowed)!;
        var thrownReferenceId =
            DocumentationCommentId.CreateReferenceId(thrown)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                allowed.ContainingAssembly.Identity.Name,
                Is.EqualTo(thrown.ContainingAssembly.Identity.Name));
            Assert.That(
                allowedDocumentationId,
                Is.EqualTo(thrownDocumentationId));
            Assert.That(allowedReferenceId, Is.EqualTo(thrownReferenceId));
            Assert.That(
                LegacyIdentity(allowed),
                Is.EqualTo(LegacyIdentity(thrown)));
            Assert.That(
                allowed.ContainingAssembly.Identity.Version,
                Is.Not.EqualTo(thrown.ContainingAssembly.Identity.Version));
            Assert.That(allowedIdentity, Is.Not.EqualTo(thrownIdentity));
            Assert.That(
                allowedIdentity,
                Does.Contain("Version=1.0.0.0")
                    .And.Contain("Culture=neutral")
                    .And.Contain("PublicKeyToken=b77a5c561934e089")
                    .And.EndWith("::" + allowedReferenceId));
            Assert.That(
                thrownIdentity,
                Does.Contain("Version=2.0.0.0")
                    .And.EndWith("::" + thrownReferenceId));
            Assert.That(
                CompilerExceptionTypeIdentity.Encode(constructedInteger),
                Is.Not.EqualTo(
                    CompilerExceptionTypeIdentity.Encode(constructedString)));
            Assert.That(
                Hierarchy(thrown),
                Does.Not.Contain(allowed)
                    .Using<INamedTypeSymbol>(SymbolEqualityComparer.Default));
        }

        var discovery = new ClaimManifestBuilder(
            compilation,
            WorkerFeatureSet.Effects).Build();
        var aliasedEvidence = discovery.Targets.Values
            .Single(target => target.Method.Name == "ThrowAliased")
            .EffectClaims.Single().Evidence;
        var frameworkEvidence = discovery.Targets.Values
            .Single(target => target.Method.Name == "ThrowFramework")
            .EffectClaims.Single().Evidence;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                aliasedEvidence.Constraint.AllowedExceptionTypes,
                Is.EqualTo([allowedIdentity]));
            Assert.That(
                aliasedEvidence.Evidence,
                Does.Contain(allowedIdentity).And.Contain(thrownIdentity));
            Assert.That(
                aliasedEvidence.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown),
                "Exact user-exception witnesses remain outside the admitted subset.");
            Assert.That(aliasedEvidence.Witness, Is.Null);
            Assert.That(
                frameworkEvidence.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                frameworkEvidence.Reason,
                Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                frameworkEvidence.Witness?.Kind,
                Is.EqualTo("explicit-throw"));
            Assert.That(
                frameworkEvidence.Witness?.ExactExceptionTypeHierarchy,
                Is.Not.Empty);
            Assert.That(
                frameworkEvidence.Replay?.Events.Single().Kind,
                Is.EqualTo(
                    CompilerEffectReplayEventKind.ExplicitThrow));
        }

        const string claimId = "effect-exception-identity";
        var evidence = new CompilerEffectClaimArtifact
        {
            ClaimId = claimId,
            ContractKind = WorkerEffectContractKind.AllowedExceptions,
            Outcome = WorkerClaimOutcome.Unknown,
            Reason = WorkerClaimReason.CounterexampleNotReplayable,
            Certainty = WorkerEffectEvidenceCertainty.Unavailable,
            Constraint = new CompilerEffectConstraintArtifact
            {
                AllowedExceptionTypes = [allowedIdentity]
            },
            Evidence =
                "canonical-exception-identity:" + thrownIdentity
        };
        CompilerEffectClaimArtifactCodec.Seal(evidence);
        var target = new CompilerCallablePreparation(
            new IrFactory(),
            new WorkerCallableManifestEntry
            {
                CallableId = "M:Subject.Throw",
                ClaimIds = [claimId]
            },
            [],
            [],
            WorkerClaimReason.None,
            CompilerPreparedBody.Trivial())
        {
            EffectClaims = [evidence]
        };

        var result = EffectClaimResultAssembler.Assemble(target, evidence);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                result.Reason,
                Is.EqualTo(
                    WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(
                result.EffectCertainty,
                Is.EqualTo(WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(result.EffectWitness, Is.Null);
            Assert.That(
                evidence.Constraint.AllowedExceptionTypes,
                Does.Contain(allowedIdentity)
                    .And.Not.Contain(thrownIdentity));
            Assert.That(
                evidence.Evidence,
                Does.Contain(thrownIdentity));
            Assert.That(result.Model, Is.Empty);
        }
    }

    [Test]
    public void ConstructedGenericExceptionEvidenceCannotReplaceBodyReplay()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            public sealed class GenericBoomException<T>
                : System.Exception {
            }

            public static class Subject {
                public static void Compare(
                    GenericBoomException<int> allowed,
                    GenericBoomException<string> thrown) {
                }
            }
            """,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            "Constructed.Exception.Consumer",
            [tree],
            PlatformReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var method = compilation.GetTypeByMetadataName("Subject")!
            .GetMembers("Compare")
            .OfType<IMethodSymbol>()
            .Single();
        var allowed = (INamedTypeSymbol)method.Parameters[0].Type;
        var thrown = (INamedTypeSymbol)method.Parameters[1].Type;
        var allowedIdentity = CompilerExceptionTypeIdentity.Encode(allowed);
        var thrownIdentity = CompilerExceptionTypeIdentity.Encode(thrown);

        var mismatched = Replay(allowedIdentity);
        var matched = Replay(thrownIdentity);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allowedIdentity, Is.Not.EqualTo(thrownIdentity));
            Assert.That(
                CompilerExceptionTypeIdentity.EncodeHierarchy(thrown),
                Does.Contain(thrownIdentity).And.Not.Contain(allowedIdentity));
            Assert.That(
                mismatched.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                mismatched.Reason,
                Is.EqualTo(
                    WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(
                matched.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                matched.Reason,
                Is.EqualTo(
                    WorkerClaimReason.CounterexampleNotReplayable));
        }

        WorkerClaimResult Replay(string allowedExceptionType)
        {
            const string claimId = "constructed-generic-exception";
            var evidence = new CompilerEffectClaimArtifact
            {
                ClaimId = claimId,
                ContractKind = WorkerEffectContractKind.AllowedExceptions,
                Outcome = WorkerClaimOutcome.Unknown,
                Reason = WorkerClaimReason.CounterexampleNotReplayable,
                Certainty =
                    WorkerEffectEvidenceCertainty.Unavailable,
                Constraint = new CompilerEffectConstraintArtifact
                {
                    AllowedExceptionTypes = [allowedExceptionType]
                },
                Evidence =
                    "constructed-generic-exception:" +
                    thrownIdentity
            };
            CompilerEffectClaimArtifactCodec.Seal(evidence);
            var target = new CompilerCallablePreparation(
                new IrFactory(),
                new WorkerCallableManifestEntry
                {
                    CallableId = "M:Subject.Compare",
                    ClaimIds = [claimId]
                },
                [],
                [],
                WorkerClaimReason.None,
                CompilerPreparedBody.Trivial())
            {
                EffectClaims = [evidence]
            };
            return EffectClaimResultAssembler.Assemble(target, evidence);
        }
    }

    private static string LegacyIdentity(INamedTypeSymbol type)
    {
        return type.ContainingAssembly.Identity.Name + ":" +
            DocumentationCommentId.CreateDeclarationId(type);
    }

    private static IEnumerable<INamedTypeSymbol> Hierarchy(
        INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static PortableExecutableReference CreateExceptionReference(
        string assemblyName,
        Version version,
        string alias)
    {
        var tree = CSharpSyntaxTree.ParseText(
            $$"""
            using System.Reflection;
            [assembly: AssemblyVersion("{{version}}")]

            namespace Collision {
                public sealed class BoomException : System.Exception {
                }

                public sealed class GenericBoomException<T>
                    : System.Exception {
                }
            }
            """,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            PlatformReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                cryptoPublicKey: EcmaPublicKey,
                delaySign: true,
                deterministic: true));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.That(
            emit.Success,
            Is.True,
            string.Join(
                Environment.NewLine,
                emit.Diagnostics.Select(static diagnostic =>
                    diagnostic.ToString())));
        return MetadataReference.CreateFromImage(
            stream.ToArray().ToImmutableArray(),
            new MetadataReferenceProperties(
                MetadataImageKind.Assembly,
                aliases: [alias]));
    }

    private static ImmutableArray<MetadataReference> PlatformReferences
    {
        get;
    } =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(static path =>
            (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static MetadataReference AttributeReference
    {
        get;
    } =
        MetadataReference.CreateFromFile(
            typeof(AllowedExceptionsAttribute).Assembly.Location);

    private static ImmutableArray<byte> EcmaPublicKey
    {
        get;
    } = [
        0, 0, 0, 0, 0, 0, 0, 0,
        4, 0, 0, 0, 0, 0, 0, 0
    ];
}
