using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

// Independent SDK identity boundary; public collector script admission is not claimed.
[TestFixture]
public sealed class CompilerScriptQueryIdentityBoundaryTests
{
    [Test]
    public async Task EmittedScriptQueryRetainsMissingSourceDeclarationRejection()
    {
        var exception = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
        {
            await FingerprintAsync(querySyntax: true);
        }));
        Assert.That(exception!.Message, Is.EqualTo("A nested method target has no lexical sibling role."));
        Assert.That(await FingerprintAsync(querySyntax: false), Has.Length.EqualTo(64));
    }

    private static async Task<string> FingerprintAsync(bool querySyntax)
    {
        var sequence = querySyntax
            ? "(from item in new[] { value } select item)"
            : "new[] { value }.Select(item => item)";
        var regular = AnalyzerTestHost.CreateCompilation("", []);
        var tree = CSharpSyntaxTree.ParseText(
            $$"""
            using System.Linq;
            int Echo(int item) => item;
            int result = 0;
            for (int value = 7; value < 8; value++) {
                result = Echo({{sequence}}.Single());
            }
            result
            """, new CSharpParseOptions(LanguageVersion.Preview, kind: SourceCodeKind.Script),
            path: "script-query-identity.cs");
        var compilation = CSharpCompilation.CreateScriptCompilation("ScriptQueryIdentity", tree,
            regular.References, regular.Options, returnType: typeof(object));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();
        var syntax = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString() == "Echo");
        var invocation = (IInvocationOperation)model.GetOperation(syntax)!;
        var owner = (IMethodSymbol)model.GetEnclosingSymbol(syntax.SpanStart)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owner.IsImplicitlyDeclared, Is.True);
            Assert.That(owner.DeclaringSyntaxReferences, Is.Empty);
            Assert.That(invocation.Arguments, Has.Length.EqualTo(1));
            Assert.That(invocation.SemanticModel, Is.Not.Null);
            Assert.That(invocation.SemanticModel!.SyntaxTree, Is.SameAs(tree));
        }
        var pending = new Stack<IOperation>();
        pending.Push(invocation);
        var anonymousParameters = new List<IParameterSymbol>();
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (current is IParameterReferenceOperation parameter &&
                parameter.Parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction })
            { anonymousParameters.Add(parameter.Parameter); }
            foreach (var child in current.ChildOperations)
            { pending.Push(child); }
        }
        Assert.That(anonymousParameters, Is.Not.Empty);
        var anonymousDeclaration = await anonymousParameters.First().ContainingSymbol
            .DeclaringSyntaxReferences.First().GetSyntaxAsync();
        Assert.That(anonymousDeclaration is AnonymousFunctionExpressionSyntax, Is.EqualTo(!querySyntax));
        var entryPoint = compilation.GetEntryPoint(CancellationToken.None)!;
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("ScriptQueryIdentity", isCollectible: true);
        try
        {
            using var image = new MemoryStream(AnalyzerTestHost.EmitImage(compilation), writable: false);
            var assembly = loadContext.LoadFromStream(image);
            var factory = assembly.GetType(entryPoint.ContainingType.MetadataName, throwOnError: true)!
                .GetMethod(entryPoint.MetadataName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            var execution = (Task<object>)factory.Invoke(null, [new object[2]])!;
            Assert.That(await execution, Is.EqualTo(7));
        }
        finally
        { loadContext.Unload(); }
        return SemanticClaimIdentity.CreateInvocationFingerprint(invocation, owner, owner, usesCompanion: false);
    }
}
