using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class PartialConstructedReturnEditingRecoveryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ConstructedPartialReturnCompatibilityUsesTheActualTargetFrame(bool coincident)
    {
        var secondArgument = coincident ? "string" : "int";
        var source = $$"""
            using System.Collections.Generic;
            using SharpProof.Attributes;
            public static partial class Target {
                public static partial List<(T First, U Second)> Read<T, U>();
                public static partial List<(U First, T Second)> Read<T, U>() {
                    Contract.Ensures(Contract.Result<List<(U First, T Second)>>() != null);
                    return new List<(U First, T Second)>();
                }
            }
            public static class Caller {
                public static void Call() { Target.Read<string, {{secondArgument}}>(); }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12,
                preprocessorSymbols: [SharpProof.Attributes.Contract.ConditionalSymbol]));
        var compilation = CSharpCompilation.Create(
            "PartialConstructedReturnEditing_" + Guid.NewGuid().ToString("N"),
            [tree], TestMetadataReferences.WithSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary,
                NullableContextOptions.Enable));
        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(syntax => syntax.Identifier.ValueText == "Read").ToArray();
        var definitionSyntax = methods.Single(syntax => syntax.Body == null);
        var implementationSyntax = methods.Single(syntax => syntax.Body != null);
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.That(errors, Has.Length.EqualTo(1));
        Assert.That(errors[0].Id, Is.EqualTo("CS8817"));
        Assert.That(errors[0].Location.SourceTree, Is.SameAs(tree));
        Assert.That(errors[0].Location.SourceSpan, Is.EqualTo(implementationSyntax.Identifier.Span));
        var definition = model.GetDeclaredSymbol(definitionSyntax)!;
        var implementation = model.GetDeclaredSymbol(implementationSyntax)!;
        Assert.That(definition.PartialImplementationPart, Is.SameAs(implementation));
        Assert.That(implementation.PartialDefinitionPart, Is.SameAs(definition));
        var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(syntax => syntax.Expression.ToString().StartsWith("Target.Read<", StringComparison.Ordinal));
        var callOperation = model.GetOperation(call);
        Assert.That(callOperation, Is.InstanceOf<IInvocationOperation>());
        var target = ((IInvocationOperation)callOperation!).TargetMethod;
        Assert.That(target.OriginalDefinition, Is.SameAs(definition));
        var actualImplementation = implementation.Construct(target.TypeArguments.ToArray());
        Assert.That(SymbolEqualityComparer.Default.Equals(target.ReturnType,
            actualImplementation.ReturnType), Is.EqualTo(coincident));
        var binder = new ContractBinder(compilation, new IrFactory());
        var inventory = binder.GetClauseInventory(target);
        Assert.That(inventory.Clauses, Has.Length.EqualTo(1));
        Assert.That(inventory.Clauses[0].Location.SourceTree, Is.SameAs(tree));
        Assert.That(implementationSyntax.Body!.Span.Contains(
            inventory.Clauses[0].Location.SourceSpan), Is.True);
        var ownImplementation = binder.Bind(actualImplementation);
        Assert.That(ownImplementation.IsSuccess, Is.True, ownImplementation.Failure.ToString());
        var result = binder.Bind(target);
        Assert.That(result.Failure, Is.EqualTo(coincident
            ? ContractBindingFailure.None : ContractBindingFailure.UnsupportedExpression));
        Assert.That(result.IsSuccess, Is.EqualTo(coincident));
        Assert.That(result.Contracts is null, Is.EqualTo(!coincident));
        if (coincident)
        {
            Assert.That(result.Contracts!.Target, Is.SameAs(target));
        }
    }
}
