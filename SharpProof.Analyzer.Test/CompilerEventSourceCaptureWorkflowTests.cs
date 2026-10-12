using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerEventSourceCaptureWorkflowTests
{
    [Test]
    public async Task OrdinarySourceDelegateInvocationPreservesIncompleteCallCollection()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            internal delegate int Callback();
            internal static class Subject {
                internal static int Invoke(Callback callback) {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    return callback();
                }
            }
            """, [], filePath: "delegate.cs");
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(item => item.Identifier.ValueText == "Invoke");
        var model = compilation.GetSemanticModel(tree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var syntax = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(item => item.Expression is IdentifierNameSyntax { Identifier.ValueText: "callback" });
        var operation = model.GetOperation(syntax);
        Assert.That(operation, Is.InstanceOf<IInvocationOperation>());
        var invocation = (IInvocationOperation)operation!;
        var callee = invocation.TargetMethod.OriginalDefinition;
        var delegateDeclaration = root.DescendantNodes().OfType<DelegateDeclarationSyntax>().Single();
        var delegateType = model.GetDeclaredSymbol(delegateDeclaration)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(callee.MethodKind, Is.EqualTo(MethodKind.DelegateInvoke));
            Assert.That(callee.DeclaringSyntaxReferences, Is.Empty);
            Assert.That(SymbolEqualityComparer.Default.Equals(callee.ContainingAssembly, compilation.Assembly), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(callee.ContainingType, delegateType), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(method.Parameters.Single().Type, delegateType), Is.True);
            Assert.That(declaration.Span.Length, Is.GreaterThan(0));
        }

        var artifact = await CaptureAsync(compilation);
        Assert.That(artifact.ReachableSource, Is.Not.Null);
        var reachable = artifact.ReachableSource!;
        var callable = artifact.Manifest.Callables.Single();
        var claim = artifact.Manifest.Claims.Single();
        var sourceRoot = reachable.Roots.Single();
        var body = reachable.Bodies.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.CallableId, Is.EqualTo(callable.CallableId));
            Assert.That(sourceRoot.CallableId, Is.EqualTo(callable.CallableId));
            Assert.That(sourceRoot.BodyId, Is.EqualTo(body.BodyId));
            Assert.That(body.MethodIdentity, Is.EqualTo(callable.CallableId));
            Assert.That(body.SourceTreeOrdinal, Is.Zero);
            Assert.That(body.Start, Is.EqualTo(declaration.SpanStart));
            Assert.That(body.Length, Is.EqualTo(declaration.Span.Length));
            Assert.That(body.Length, Is.GreaterThan(0));
            Assert.That(body.CallsComplete, Is.False);
            Assert.That(reachable.CollectionComplete, Is.False);
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
