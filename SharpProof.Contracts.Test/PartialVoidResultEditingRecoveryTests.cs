using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class PartialVoidResultEditingRecoveryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PartialVoidDefinitionRejectsAnImplementationResultClause(bool mismatched)
    {
        var returnType = mismatched ? "void" : "bool";
        var source = $$"""
            using SharpProof.Attributes;
            public static partial class Target {
                public static partial {{returnType}} Read();
                public static partial bool Read() {
                    Contract.Ensures(Contract.Result<bool>());
                    return true;
                }
            }
            public static class Caller {
                public static void Call() { Target.Read(); }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12,
                preprocessorSymbols: [Contract.ConditionalSymbol]));
        var compilation = CSharpCompilation.Create("ContractsPartialVoidResultEditing",
            [tree], TestMetadataReferences.WithSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        var root = tree.GetRoot();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(static syntax => syntax.Identifier.ValueText == "Read").ToArray();
        var definitionSyntax = methods.Single(static syntax => syntax.Body == null);
        var implementationSyntax = methods.Single(static syntax => syntax.Body != null);
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
        }
        var model = compilation.GetSemanticModel(tree);
        var definition = model.GetDeclaredSymbol(definitionSyntax)!;
        var implementation = model.GetDeclaredSymbol(implementationSyntax)!;
        Assert.That(definition.PartialImplementationPart, Is.SameAs(implementation));
        Assert.That(implementation.PartialDefinitionPart, Is.SameAs(definition));
        Assert.That(implementation.ReturnsVoid, Is.False);
        Assert.That(implementation.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        var callerSyntax = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(syntax => syntax.Expression.ToString() == "Target.Read");
        var callerOperation = model.GetOperation(callerSyntax);
        Assert.That(callerOperation, Is.InstanceOf<IInvocationOperation>());
        var target = ((IInvocationOperation)callerOperation!).TargetMethod;
        Assert.That(target, Is.SameAs(definition));
        Assert.That(target.ReturnsVoid, Is.EqualTo(mismatched));
        Assert.That(target.ReturnType.SpecialType,
            Is.EqualTo(mismatched ? SpecialType.System_Void : SpecialType.System_Boolean));
        var resultSyntax = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(syntax => syntax.Expression.ToString() == "Contract.Result<bool>");
        var resultOperation = model.GetOperation(resultSyntax);
        Assert.That(resultOperation, Is.InstanceOf<IInvocationOperation>());
        Assert.That(resultOperation!.Type!.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(model.GetEnclosingSymbol(resultSyntax.SpanStart), Is.SameAs(implementation));
        var factory = new IrFactory();
        var binder = new ContractBinder(compilation, factory);
        var inventory = binder.GetClauseInventory(target);
        Assert.That(inventory.Callable, Is.SameAs(implementation));
        Assert.That(inventory.Callable.ReturnsVoid, Is.False);
        Assert.That(inventory.Callable.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(inventory.HasRejectedContractApiUsage, Is.False);
        Assert.That(inventory.ImplementationBody, Is.Not.Null);
        Assert.That(inventory.Clauses, Has.Length.EqualTo(1));
        Assert.That(inventory.Clauses[0].Kind, Is.EqualTo(BoundContractKind.Ensures));
        Assert.That(inventory.Clauses[0].IsValid, Is.True);
        var inventoriedResult = inventory.Clauses[0].Invocation.Arguments.Single().Value;
        Assert.That(inventoriedResult, Is.InstanceOf<IInvocationOperation>());
        Assert.That(inventoriedResult.Syntax.Span, Is.EqualTo(resultSyntax.Span));
        Assert.That(inventoriedResult.Type!.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(model.GetEnclosingSymbol(inventoriedResult.Syntax.SpanStart), Is.SameAs(implementation));
        var result = binder.Bind(target);
        Assert.That(result.Failure, Is.EqualTo(mismatched
            ? ContractBindingFailure.UnsupportedExpression : ContractBindingFailure.None));
        Assert.That(result.IsSuccess, Is.EqualTo(!mismatched));
        if (mismatched)
        {
            Assert.That(result.Contracts, Is.Null);
        }
        else
        {
            Assert.That(result.Contracts!.Clauses, Has.Length.EqualTo(1));
            var resultVariable = result.Contracts.Variables
                .Single(variable => variable.Role == BoundContractVariableRole.Result).Variable;
            foreach (var value in new[] { false, true })
            {
                var evaluation = new IrInterpreter(factory).Evaluate(result.Contracts.Clauses[0].Condition,
                    new Dictionary<IrVarId, IrValue> { [resultVariable] = factory.CreateBooleanValue(value) });
                Assert.That(evaluation.Value!.Boolean, Is.EqualTo(value));
            }
        }
    }
}
