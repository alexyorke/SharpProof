using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275LunaCollectorSourceCoverageTests
{
    [Test]
    public void ArithmeticPostconditionWithStaticFieldRetainsItsAdmissionResult()
    {
        var compilation = TestCompilation.Create(
            "Pr275LunaCollectorArithmeticPostcondition",
            ("Subject.cs", """
                using SharpProof.Attributes;
                internal static class Subject {
                    private static readonly int Offset = 5;

                    [DoesNotThrow]
                    internal static int Adjust(int value) {
                        Contract.Ensures(
                            Contract.Result<int>() == value + Offset);
                        if (value < 0) {
                            return Offset + value;
                        }
                        return value + Offset;
                    }
                }
                """));
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values
            .Single(item => item.Method.Name == "Adjust");
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory())
            .Prepare(target, cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(preparation.IsSuccess, Is.False);
        Assert.That(preparation.FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
        Assert.That(preparation.Total, Is.Null);
    }

    [Test]
    public void CapturedScalarContractKeepsControlFlowAndReadOnlyInitializationEvidence()
    {
        var compilation = TestCompilation.Create(
            "Pr275LunaCollectorScalar",
            ("Subject.cs", """
                using SharpProof.Attributes;
                internal static class Subject {
                    private static readonly int Offset = 5;

                    [DoesNotThrow]
                    internal static int Adjust(int value) {
                        Contract.Ensures(
                            Contract.Result<int>() == value);
                        if (value < Offset) {
                            return value;
                        }
                        return value;
                    }
                }
                """));
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var target = discovery.Targets.Values.Single(item =>
            item.Method.Name == "Adjust");
        var trees = CompilerCompilationCapture.CaptureTrees(
            compilation,
            TestContext.CurrentContext.CancellationToken);
        var references = CompilerCompilationCapture.CaptureReferences(
            compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default,
            TestContext.CurrentContext.CancellationToken);
        var packs = CompilerSpecificationPackProvider.ResolveConfiguration(null);
        var preparation = new CompilerCallableLowerer(
            compilation,
            new IrFactory(),
            packs,
            trees,
            references).Prepare(target, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var artifact = CreateArtifact(compilation, discovery);
        var decoded = CompilerManifestArtifactJson.DecodeCallables(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)
            .Single(item => item.Entry.CallableId == target.Entry.CallableId);

        Assert.That(preparation.IsSuccess, Is.True, preparation.FailureReason.ToString());
        Assert.That(preparation.Total, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(trees, Has.Length.EqualTo(1));
            Assert.That(references, Is.Not.Empty);
            Assert.That(preparation.Total!.Program.Factory.Semantics,
                Is.EqualTo(IrExecutionSemantics.Total));
            Assert.That(preparation.Total.EffectsCompleteAtEntry, Is.True);
            Assert.That(decoded.Total, Is.Not.Null);
            Assert.That(artifact.Compilation.SyntaxTrees, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public void SourceConstructorAndAccessorsKeepTheirEffectClaimsInTheArtifact()
    {
        var compilation = TestCompilation.Create(
            "Pr275LunaCollectorAccessors",
            ("Subject.cs", """
                using SharpProof.Attributes;
                internal sealed class Subject {
                    private int _value;

                    [DoesNotThrow]
                    internal Subject(int value) {
                        _value = value;
                    }

                    internal int Value {
                        [DoesNotThrow]
                        get { return _value; }

                        [DoesNotThrow]
                        set { _value = value; }
                    }
                }
                """));
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var targets = discovery.Targets.Values
            .Where(item => item.Method.MethodKind is
                MethodKind.Constructor or MethodKind.PropertyGet or MethodKind.PropertySet)
            .ToArray();
        var artifact = CreateArtifact(compilation, discovery);
        var callables = artifact.Callables.ToDictionary(
            item => item.CallableId,
            StringComparer.Ordinal);

        Assert.That(targets.Select(item => item.Method.MethodKind), Is.EquivalentTo(new[]
        {
            MethodKind.Constructor,
            MethodKind.PropertyGet,
            MethodKind.PropertySet
        }));
        foreach (var target in targets)
        {
            var manifestClaims = target.EffectClaims
                .Select(item => item.Evidence.ClaimId)
                .ToArray();
            var artifactClaims = callables[target.Entry.CallableId].EffectClaims
                .Select(item => item.ClaimId)
                .ToArray();

            Assert.That(manifestClaims, Has.Length.EqualTo(1), target.Method.MethodKind.ToString());
            Assert.That(artifactClaims, Is.EqualTo(manifestClaims), target.Method.MethodKind.ToString());
        }

        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void AttributedFieldInitializerLambdaIsDiscoveredWithItsSourceOwner()
    {
        var compilation = TestCompilation.Create(
            "Pr275LunaCollectorFieldLambda",
            ("Subject.cs", """
                using System;
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static readonly Action Callback =
                        [DoesNotThrow] static () => { };
                }
                """));
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var target = discovery.Targets.Values.Single();
        var artifact = CreateArtifact(compilation, discovery);
        var callable = artifact.Callables.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Method.MethodKind, Is.EqualTo(MethodKind.AnonymousFunction));
            Assert.That(target.Method.ContainingSymbol, Is.Not.InstanceOf<IMethodSymbol>());
            Assert.That(target.Entry.CallableId, Is.Not.Empty);
            Assert.That(callable.CallableId, Is.EqualTo(target.Entry.CallableId));
            Assert.That(target.EffectClaims, Has.Length.EqualTo(1));
            Assert.That(callable.EffectClaims.Select(item => item.ClaimId),
                Is.EqualTo(target.EffectClaims.Select(item => item.Evidence.ClaimId)));
        }
    }

    private static CompilerManifestArtifact CreateArtifact(
        CSharpCompilation compilation,
        ClaimManifestBuildResult discovery)
    {
        return CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net8.0",
            WorkerFeatureSet.All,
            discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth,
            TestContext.CurrentContext.CancellationToken);
    }
}
