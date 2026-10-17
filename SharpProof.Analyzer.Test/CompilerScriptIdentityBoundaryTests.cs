using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

// Independent SDK identity boundary; script admission to the collector is not claimed.
[TestFixture]
public sealed class CompilerScriptIdentityBoundaryTests
{
    [Test]
    public async Task EmittedScriptLocalRetainsMissingOwnerFallbackAcrossRenames()
    {
        var first = await FingerprintAsync("value", 0);
        var renamed = await FingerprintAsync("renamed", 0);
        var differentArgument = await FingerprintAsync("value", 1);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Length.EqualTo(64));
            Assert.That(renamed, Is.EqualTo(first));
            Assert.That(differentArgument, Is.Not.EqualTo(first));
        }
    }

    private static async Task<string> FingerprintAsync(string variable, int offset)
    {
        System.ArgumentNullException.ThrowIfNull(variable);
        var regular = AnalyzerTestHost.CreateCompilation("", []);
        var tree = CSharpSyntaxTree.ParseText(
            $$"""
            int Echo(int item) => item;
            int result = 0;
            for (int {{variable}} = 7; {{variable}} < 8; {{variable}}++) {
                result = Echo({{variable}} + {{offset}});
            }
            result
            """, new CSharpParseOptions(LanguageVersion.Preview, kind: SourceCodeKind.Script),
            path: "script-identity.cs");
        var compilation = CSharpCompilation.CreateScriptCompilation("ScriptIdentity", tree,
            regular.References, regular.Options, returnType: typeof(object));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();
        var syntax = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(syntax)!;
        var owner = (IMethodSymbol)model.GetEnclosingSymbol(syntax.SpanStart)!;
        var localSyntax = root.DescendantNodes().OfType<ForStatementSyntax>()
            .Single().Declaration!.Variables.Single();
        var local = (ILocalSymbol)model.GetDeclaredSymbol(localSyntax)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owner.IsImplicitlyDeclared, Is.True);
            Assert.That(owner.DeclaringSyntaxReferences, Is.Empty);
            Assert.That(SymbolEqualityComparer.Default.Equals(local.ContainingSymbol, owner), Is.True);
            Assert.That(local.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
            Assert.That(invocation.Arguments, Has.Length.EqualTo(1));
            Assert.That(invocation.Arguments.Single().Value, Is.InstanceOf<IBinaryOperation>());
            Assert.That(((IBinaryOperation)invocation.Arguments.Single().Value).LeftOperand,
                Is.InstanceOf<ILocalReferenceOperation>());
        }
        var entryPoint = compilation.GetEntryPoint(CancellationToken.None)!;
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("ScriptIdentity", isCollectible: true);
        try
        {
            using var image = new MemoryStream(AnalyzerTestHost.EmitImage(compilation), writable: false);
            var assembly = loadContext.LoadFromStream(image);
            var factory = assembly.GetType(entryPoint.ContainingType.MetadataName, throwOnError: true)!
                .GetMethod(entryPoint.MetadataName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            var execution = (Task<object>)factory.Invoke(null, [new object[2]])!;
            Assert.That(await execution, Is.EqualTo(7 + offset));
        }
        finally
        {
            loadContext.Unload();
        }
        return SemanticClaimIdentity.CreateInvocationFingerprint(invocation, owner, owner, usesCompanion: false);
    }
}
