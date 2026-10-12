using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerEventSourceCaptureWorkflowTests
{
    [TestCase(false, 1)]
    [TestCase(true, 0)]
    public async Task SourceInitializationCycleSuppressesOnlyTheEntryExceptionConstraint(
        bool hasCycle, int expectedConstraintCount)
    {
        var source = """
            using SharpProof.Attributes;
            internal static class A {
                internal static int State;
                static A() { B.State = 1; }
                [DoesNotThrow]
                internal static int Root() => 7;
            }
            internal static class B {
                internal static int State;
            """ + (hasCycle ? "static B() { A.State = 1; }" : "") + "}";
        var compilation = AnalyzerTestHost.CreateCompilation(source, [], filePath: "initialization-cycle.cs");
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var a = compilation.GetTypeByMetadataName("A")!;
        var b = compilation.GetTypeByMetadataName("B")!;
        Assert.That(a.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(b.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(a.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(b.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(a.StaticConstructors, Has.Length.EqualTo(1));
        Assert.That(b.StaticConstructors, Has.Length.EqualTo(hasCycle ? 1 : 0));
        var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ToArray();
        Assert.That(constructors, Has.Length.EqualTo(hasCycle ? 2 : 1));
        foreach (var constructor in constructors)
        {
            var owner = (IMethodSymbol)model.GetDeclaredSymbol(constructor)!;
            Assert.That(owner.MethodKind, Is.EqualTo(MethodKind.StaticConstructor));
            Assert.That(owner.IsImplicitlyDeclared, Is.False);
            var assignmentSyntax = constructor.Body!.Statements.OfType<ExpressionStatementSyntax>().Single().Expression;
            var operation = model.GetOperation(assignmentSyntax);
            Assert.That(operation, Is.InstanceOf<ISimpleAssignmentOperation>());
            var assignment = (ISimpleAssignmentOperation)operation!;
            Assert.That(assignment.IsRef, Is.False);
            Assert.That(assignment.Target, Is.InstanceOf<IFieldReferenceOperation>());
            var field = (IFieldReferenceOperation)assignment.Target;
            Assert.That(field.Field.IsStatic, Is.True);
            Assert.That(field.Instance, Is.Null);
            Assert.That(assignment.Value.ConstantValue.HasValue, Is.True);
            Assert.That(assignment.Value.ConstantValue.Value, Is.EqualTo(1));
            Assert.That(SymbolEqualityComparer.Default.Equals(field.Field.ContainingType,
                SymbolEqualityComparer.Default.Equals(owner.ContainingType, a) ? b : a), Is.True);
        }
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        Assert.That(model.GetOperation(declaration), Is.InstanceOf<IMethodBodyOperation>());
        Assert.That(declaration.Span.Length, Is.GreaterThan(0));

        var artifact = await CaptureAsync(compilation);
        var callable = artifact.Manifest.Callables.Single();
        var claim = artifact.Manifest.Claims.Single();
        var prepared = artifact.Callables.Single();
        Assert.That(prepared.Total, Is.Not.Null);
        var total = prepared.Total!;
        Assert.That(claim.Kind, Is.EqualTo(WorkerClaimKind.Effect));
        Assert.That(claim.EffectContractKind, Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
        Assert.That(claim.CallableId, Is.EqualTo(callable.CallableId));
        Assert.That(prepared.CallableId, Is.EqualTo(callable.CallableId));
        Assert.That(prepared.FailureReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(prepared.EffectClaims.Single().ClaimId, Is.EqualTo(claim.ClaimId));
        Assert.That(total.IsBodyAbstraction, Is.False);
        Assert.That(total.ValidEffectClaimIds, Is.EqualTo(new[] { claim.ClaimId }));
        Assert.That(total.ExceptionConstraints, Has.Length.EqualTo(expectedConstraintCount));
        if (!hasCycle)
        {
            Assert.That(total.ExceptionConstraints.Single().ClaimId, Is.EqualTo(claim.ClaimId));
            Assert.That(total.ExceptionConstraints.Single().AllowedKinds, Is.Empty);
        }
        Assert.That(artifact.ReachableSource, Is.Not.Null);
        var reachable = artifact.ReachableSource!;
        var sourceRoot = reachable.Roots.Single();
        var body = reachable.Bodies.Single();
        Assert.That(sourceRoot.CallableId, Is.EqualTo(callable.CallableId));
        Assert.That(sourceRoot.BodyId, Is.EqualTo(body.BodyId));
        Assert.That(body.MethodIdentity, Is.EqualTo(callable.CallableId));
        Assert.That(body.SourceTreeOrdinal, Is.Zero);
        Assert.That(body.Start, Is.EqualTo(declaration.SpanStart));
        Assert.That(body.Length, Is.EqualTo(declaration.Span.Length));
        Assert.That(body.Graph, Is.Not.Null);
        Assert.That(body.CallsComplete, Is.True);
        Assert.That(reachable.CollectionComplete, Is.True);
        Assert.That(artifact.CompilerDiagnostics, Is.Empty);
    }

    [Test]
    public async Task UninitializedStaticCustomEventPreservesDoesNotThrowEntryConstraint()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;
            internal static class Subject {
                internal static event Action Changed { add { } remove { } }
                [DoesNotThrow]
                internal static int Root() => 1;
            }
            """, [], filePath: "custom-event.cs");
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var eventDeclaration = root.DescendantNodes().OfType<EventDeclarationSyntax>().Single();
        var eventSymbol = (IEventSymbol)model.GetDeclaredSymbol(eventDeclaration)!;
        var eventSource = await eventSymbol.DeclaringSyntaxReferences.Single().GetSyntaxAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(eventSymbol.IsStatic, Is.True);
            Assert.That(eventSymbol.Type.TypeKind, Is.EqualTo(TypeKind.Delegate));
            Assert.That(SymbolEqualityComparer.Default.Equals(eventSymbol.ContainingAssembly, compilation.Assembly), Is.True);
            Assert.That(eventSource, Is.InstanceOf<EventDeclarationSyntax>());
            Assert.That(eventSource.Span, Is.EqualTo(eventDeclaration.Span));
            Assert.That(eventSymbol.ContainingType.StaticConstructors, Is.Empty);
            Assert.That(eventDeclaration.AccessorList!.Accessors, Has.Count.EqualTo(2));
            Assert.That(eventDeclaration.AccessorList.Accessors.All(accessor => accessor.Body is { Statements.Count: 0 }), Is.True);
            Assert.That(model.GetOperation(declaration), Is.InstanceOf<IMethodBodyOperation>());
            Assert.That(method.IsStatic, Is.True);
            Assert.That(declaration.Span.Length, Is.GreaterThan(0));
        }

        var artifact = await CaptureAsync(compilation);
        var callable = artifact.Manifest.Callables.Single();
        var claim = artifact.Manifest.Claims.Single();
        var prepared = artifact.Callables.Single();
        Assert.That(prepared.Total, Is.Not.Null);
        var total = prepared.Total!;
        var constraint = total.ExceptionConstraints.Single();
        var effect = prepared.EffectClaims.Single();
        Assert.That(artifact.ReachableSource, Is.Not.Null);
        var reachable = artifact.ReachableSource!;
        var sourceRoot = reachable.Roots.Single();
        var body = reachable.Bodies.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(claim.EffectContractKind, Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
            Assert.That(claim.CallableId, Is.EqualTo(callable.CallableId));
            Assert.That(prepared.CallableId, Is.EqualTo(callable.CallableId));
            Assert.That(prepared.FailureReason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(effect.ClaimId, Is.EqualTo(claim.ClaimId));
            Assert.That(effect.ContractKind, Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
            Assert.That(total.IsBodyAbstraction, Is.False);
            Assert.That(total.ValidEffectClaimIds, Is.EqualTo(new[] { claim.ClaimId }));
            Assert.That(constraint.ClaimId, Is.EqualTo(claim.ClaimId));
            Assert.That(constraint.AllowedKinds, Is.Empty);
            Assert.That(sourceRoot.CallableId, Is.EqualTo(callable.CallableId));
            Assert.That(sourceRoot.BodyId, Is.EqualTo(body.BodyId));
            Assert.That(body.MethodIdentity, Is.EqualTo(callable.CallableId));
            Assert.That(body.SourceTreeOrdinal, Is.Zero);
            Assert.That(body.Start, Is.EqualTo(declaration.SpanStart));
            Assert.That(body.Length, Is.EqualTo(declaration.Span.Length));
            Assert.That(body.Graph, Is.Not.Null);
            Assert.That(body.CallsComplete, Is.True);
            Assert.That(reachable.CollectionComplete, Is.True);
            Assert.That(artifact.CompilerDiagnostics, Is.Empty);
        }
    }

    [Test]
    public async Task OrdinarySourceEventSubscriptionPreservesIncompleteCallCollection()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;
            internal static class Subject {
                internal static event Action Changed { add { } remove { } }
                private static void Handler() { }
                internal static int Subscribe() {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    Changed += Handler;
                    return 1;
                }
            }
            """, [], filePath: "event.cs");
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var syntax = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax);
        Assert.That(operation, Is.InstanceOf<IEventAssignmentOperation>());
        var assignment = (IEventAssignmentOperation)operation!;
        Assert.That(assignment.Adds, Is.True);
        Assert.That(assignment.EventReference, Is.InstanceOf<IEventReferenceOperation>());
        var reference = (IEventReferenceOperation)assignment.EventReference;
        var accessor = reference.Event.AddMethod!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accessor.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
            Assert.That(SymbolEqualityComparer.Default.Equals(accessor.ContainingAssembly, compilation.Assembly), Is.True);
        }

        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(item => item.Identifier.ValueText == "Subscribe");
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        var artifact = await CaptureAsync(compilation);
        Assert.That(artifact.ReachableSource, Is.Not.Null);
        var reachable = artifact.ReachableSource!;
        var callable = artifact.Manifest.Callables.Single();
        var body = reachable.Bodies.Single(item => item.MethodIdentity == callable.CallableId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(reachable.Roots.Single().CallableId, Is.EqualTo(callable.CallableId));
            Assert.That(body.SourceTreeOrdinal, Is.Zero);
            Assert.That(body.Start, Is.EqualTo(declaration.SpanStart));
            Assert.That(body.Length, Is.EqualTo(declaration.Span.Length));
            Assert.That(body.CallsComplete, Is.False);
            Assert.That(reachable.CollectionComplete, Is.False);
            Assert.That(artifact.CompilerDiagnostics, Is.Empty);
        }
    }

    private static async Task<CompilerManifestArtifact> CaptureAsync(CSharpCompilation compilation)
    {
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofEventCapture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            const string invocation = "00112233445566778899aabbccddeeff";
            await File.WriteAllTextAsync(request, invocation);
            var options = new Dictionary<string, string>
            {
                ["build_property.SharpProofProfile"] = "advisory",
                ["build_property.SharpProofFeatures"] = "all",
                ["build_property.SharpProofVerifyPolicy"] = "advisory",
                ["build_property.SharpProofAssumptionPolicy"] = "allow",
                ["build_property._SharpProofCompilerManifestPath"] = path,
                ["build_property._SharpProofCompilationTargetFramework"] = "net8.0",
                ["build_property._SharpProofProjectDirectory"] = directory,
                ["build_property.SharpProofVerifyMaximumExpressionDepth"] = "64",
                ["build_property._SharpProofCompilerCaptureRequestPath"] = request
            };
            var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, options,
                analyzer: new FinalCompilationCollectorAnalyzer());
            Assert.That(diagnostics, Is.Empty);
            Assert.That(await File.ReadAllTextAsync(request + ".completed"), Is.EqualTo(invocation));
            return CompilerManifestArtifactJson.Deserialize(await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
