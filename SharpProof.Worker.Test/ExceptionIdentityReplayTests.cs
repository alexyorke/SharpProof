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
    public void ConstructedGenericExceptionIdentityIncludesArgumentAssembly()
    {
        var firstReference = CreateExceptionReference(
            "Collision.Exceptions",
            new Version(1, 0, 0, 0),
            "first");
        var secondReference = CreateExceptionReference(
            "Collision.Exceptions",
            new Version(2, 0, 0, 0),
            "second");
        var tree = CSharpSyntaxTree.ParseText(
            """
            extern alias first;
            extern alias second;

            public static class Subject {
                public static void Compare(
                    first::Collision.GenericBoomException<
                        first::Collision.Marker> firstValue,
                    first::Collision.GenericBoomException<
                        second::Collision.Marker> secondValue) {
                }
            }
            """,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            "Generic.Argument.Consumer",
            [tree],
            TestMetadataReferences.Platform.Add(firstReference).Add(secondReference),
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
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
        var first = (INamedTypeSymbol)method.Parameters[0].Type;
        var second = (INamedTypeSymbol)method.Parameters[1].Type;
        var firstIdentity = CompilerExceptionTypeIdentity.Encode(first);
        var secondIdentity = CompilerExceptionTypeIdentity.Encode(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                DocumentationCommentId.CreateReferenceId(first),
                Is.EqualTo(
                    DocumentationCommentId.CreateReferenceId(second)));
            Assert.That(firstIdentity, Is.Not.EqualTo(secondIdentity));
            Assert.That(firstIdentity, Does.Not.Contain("Version=2.0.0.0"));
            Assert.That(secondIdentity, Does.Contain("Version=2.0.0.0"));
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
            TestMetadataReferences.Platform,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
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
                new WorkerCallableManifestEntry
                {
                    CallableId = "M:Subject.Compare",
                    ClaimIds = [claimId]
                },
                WorkerClaimReason.None)
            {
                EffectClaims = [evidence]
            };
            return EffectClaimResultAssembler.Assemble(
                target, evidence, CallableEntryFeasibility.Feasible,
                CancellationToken.None);
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

                public sealed class Marker {
                }
            }
            """,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            TestMetadataReferences.Platform,
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

    private static ImmutableArray<byte> EcmaPublicKey
    {
        get;
    } = [
        0, 0, 0, 0, 0, 0, 0, 0,
        4, 0, 0, 0, 0, 0, 0, 0
    ];
}
