using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class PartialReferenceReturnEditingRecoveryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PartialDefinitionRejectsAnImplementationWithADifferentReferenceResult(bool mismatched)
    {
        var returnArgument = mismatched ? "int" : "string";
        var source = $$"""
            using System.Collections.Generic;
            using SharpProof.Attributes;
            public static partial class Target {
                public static partial List<{{returnArgument}}> Read();
                public static partial List<string> Read() {
                    Contract.Ensures(Contract.Result<List<string>>() != null);
                    return new List<string>();
                }
            }
            public static class Caller {
                public static void Call() { Target.Read(); }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12,
                preprocessorSymbols: [SharpProof.Attributes.Contract.ConditionalSymbol]));
        var compilation = CSharpCompilation.Create(
            "PartialReferenceReturnEditing_" + Guid.NewGuid().ToString("N"),
            [tree], TestMetadataReferences.WithSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary,
                NullableContextOptions.Enable));
        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (syntax.Identifier.ValueText == "Read")).ToImmutableArray();
        var definitionSyntax = methods.Single(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (syntax.Body == null));
        var implementationSyntax = methods.Single(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (syntax.Body != null));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        if (mismatched)
        {
            Assert.That(errors, Has.Length.EqualTo(1));
            Assert.That(errors[0].Id, Is.EqualTo("CS8817"));
            Assert.That(errors[0].Location.SourceTree, Is.SameAs(tree));
            Assert.That(errors[0].Location.SourceSpan, Is.EqualTo(implementationSyntax.Identifier.Span));
        }
        else
        {
            Assert.That(errors, Is.Empty);
            using var image = new MemoryStream();
            Assert.That(compilation.Emit(image).Success, Is.True);
        }
        var definition = model.GetDeclaredSymbol(definitionSyntax)!;
        var implementation = model.GetDeclaredSymbol(implementationSyntax)!;
        Assert.That(definition.PartialImplementationPart, Is.SameAs(implementation));
        Assert.That(implementation.PartialDefinitionPart, Is.SameAs(definition));
        var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (syntax.Expression.ToString() == "Target.Read"));
        var callOperation = model.GetOperation(call);
        Assert.That(callOperation, Is.InstanceOf<IInvocationOperation>());
        var target = ((IInvocationOperation)callOperation!).TargetMethod;
        Assert.That(target, Is.SameAs(definition));
        var binder = new ContractBinder(compilation, new IrFactory());
        Assert.That(binder.GetClauseInventory(target).Callable, Is.SameAs(implementation));
        var ownImplementation = binder.Bind(implementation);
        Assert.That(ownImplementation.IsSuccess, Is.True, ownImplementation.Failure.ToString());
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var result = new ContractBinder(compilation, factory).BindTotal(new TotalLoweringContext(factory, target));
        // The Total binder rejects constructed partial targets outright.
        Assert.That(result.IsSuccess, Is.False);
    }
}
