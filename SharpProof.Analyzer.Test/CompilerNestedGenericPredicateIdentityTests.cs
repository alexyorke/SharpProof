using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerNestedGenericPredicateIdentityTests
{
    [Test]
    public void ManifestRetainsDescendantGenericRolesInsideAnExecutedPredicate()
    {
        var first = Capture("TFirst", "TSecond", selectedOrdinal: 0, expectedValue: 7);
        var renamed = Capture("TRenamedFirst", "TRenamedSecond", selectedOrdinal: 0, expectedValue: 7);
        var second = Capture("TFirst", "TSecond", selectedOrdinal: 1, expectedValue: 7);
        var changedPredicate = Capture("TFirst", "TSecond", selectedOrdinal: 0, expectedValue: 8);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Fingerprint, Has.Length.EqualTo(64));
            Assert.That(renamed, Is.EqualTo(first));
            Assert.That(second.Fingerprint, Is.Not.EqualTo(first.Fingerprint));
            Assert.That(second.ClaimId, Is.Not.EqualTo(first.ClaimId));
            Assert.That(changedPredicate.Fingerprint, Is.Not.EqualTo(first.Fingerprint));
            Assert.That(changedPredicate.ClaimId, Is.Not.EqualTo(first.ClaimId));
        }
    }

    private static (string Fingerprint, string ClaimId) Capture(
        string firstType, string secondType, int selectedOrdinal, int expectedValue)
    {
        var selectedType = selectedOrdinal == 0 ? firstType : secondType;
        var expectedResult = expectedValue == 7;
        var compilation = AnalyzerTestHost.CreateCompilation(
            $$"""
            #define SHARPPROOF_CONTRACTS
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Observed = {{(expectedResult ? "false" : "true")}};
                public static bool Root() {
                    Contract.Ensures(((Func<bool>)(() => {
                        {{selectedType}} Echo<{{firstType}}, {{secondType}}>({{selectedType}} input) => input;
                        return Observed = Echo<int, int>(7) == {{expectedValue}};
                    }))());
                    return Observed;
                }
            }
            """, [], filePath: "nested-generic-predicate.cs");
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);

        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var rootSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var owner = (IMethodSymbol)model.GetDeclaredSymbol(rootSyntax)!;
        var localSyntax = rootSyntax.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var local = (IMethodSymbol)model.GetDeclaredSymbol(localSyntax)!;
        var lambdaSyntax = rootSyntax.DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>().Single();
        var lambda = (IAnonymousFunctionOperation)model.GetOperation(lambdaSyntax)!;
        var clauseSyntax = rootSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(static syntax => syntax.Expression.ToString() == "Contract.Ensures");
        var clause = (IInvocationOperation)model.GetOperation(clauseSyntax)!;
        var references = clause.Arguments.Single().Value.DescendantsAndSelf()
            .OfType<IParameterReferenceOperation>().ToArray();
        Assert.That(references, Has.Length.EqualTo(1));
        var reference = references.Single();
        Assert.That(reference.Type, Is.InstanceOf<ITypeParameterSymbol>());
        var parameterType = (ITypeParameterSymbol)reference.Type!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(local.MethodKind, Is.EqualTo(MethodKind.LocalFunction));
            Assert.That(local.Arity, Is.EqualTo(2));
            Assert.That(SymbolEqualityComparer.Default.Equals(local.ContainingSymbol, lambda.Symbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(lambda.Symbol.ContainingSymbol, owner), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(parameterType.ContainingSymbol, local), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(reference.Parameter.ContainingSymbol, local), Is.True);
            Assert.That(parameterType.Ordinal, Is.EqualTo(selectedOrdinal));
            Assert.That(parameterType.TypeParameterKind, Is.EqualTo(TypeParameterKind.Method));
            Assert.That(SymbolEqualityComparer.Default.Equals(parameterType.ContainingSymbol, owner), Is.False);
            Assert.That(SymbolEqualityComparer.Default.Equals(model.GetEnclosingSymbol(clauseSyntax.SpanStart), owner), Is.True);
        }

        var image = AnalyzerTestHost.EmitImage(compilation);
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("NestedGenericPredicateControl", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            var subject = runtime.LoadFromStream(stream).GetType("Subject", throwOnError: true)!;
            var observed = subject.GetField("Observed")!;
            Assert.That(observed.GetValue(null), Is.EqualTo(!expectedResult));
            Assert.That(subject.GetMethod("Root")!.Invoke(null, null), Is.EqualTo(expectedResult));
            Assert.That(observed.GetValue(null), Is.EqualTo(expectedResult));
        }
        finally
        {
            runtime.Unload();
        }

        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Contracts).Build();
        Assert.That(discovery.Targets.ContainsKey(owner), Is.True);
        var target = discovery.Targets[owner];
        Assert.That(target.Claims, Has.Length.EqualTo(1));
        var claim = target.Claims.Single();
        var fingerprint = SemanticClaimIdentity.CreateInvocationFingerprint(clause, owner, owner, usesCompanion: false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.SourceOperation, Is.Not.Null);
            Assert.That(claim.SourceOperation!.Syntax.Span, Is.EqualTo(clauseSyntax.Span));
            Assert.That(claim.Placement, Is.EqualTo(SharpProof.Contracts.ContractClausePlacement.ValidPrologue));
            Assert.That(claim.Entry.Kind, Is.EqualTo(WorkerClaimKind.Postcondition));
            Assert.That(claim.Entry.Evidence, Is.EqualTo(WorkerClaimEvidence.DirectClause));
            Assert.That(claim.Entry.Ordinal, Is.Zero);
            Assert.That(claim.Entry.ClaimId, Is.EqualTo(SemanticClaimIdentity.Create(
                compilation.AssemblyName!, target.Entry.CallableId, fingerprint, duplicateRank: 0)));
            Assert.That(discovery.Manifest.Claims.Select(static entry => entry.ClaimId),
                Does.Contain(claim.Entry.ClaimId));
        }
        return (fingerprint, claim.Entry.ClaimId);
    }
}
