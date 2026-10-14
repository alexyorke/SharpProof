using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class PartialConstructedOwnerFrameTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ConstructedPartialMethodsRetainTheClosedOwnerAndRequestedFrame(bool isStatic, bool generic)
    {
        var returnType = generic ? "List<(T First, U Second, X Third)>" : "List<X>";
        var typeParameters = generic ? "<T, U>" : "";
        var typeArguments = generic ? "<int, bool>" : "";
        var modifier = isStatic ? "static " : "";
        var receiverClause = isStatic ? "" : "Contract.Requires(this != null);";
        var receiver = isStatic ? "Target<string>" : "new Target<string>()";
        var compilation = TestCompilation.Create("PartialConstructedOwnerFrame", $$"""
            using System.Collections.Generic;
            using SharpProof.Attributes;
            public partial class Target<X> {
                public {{modifier}}partial {{returnType}} Read{{typeParameters}}(List<X> value);
                public {{modifier}}partial {{returnType}} Read{{typeParameters}}(List<X> value) {
                    {{receiverClause}}
                    Contract.Requires(value != null);
                    Contract.Ensures(Contract.Result<{{returnType}}>() != null);
                    Contract.Ensures(Contract.Old(value) != null);
                    return new {{returnType}}();
                }
            }
            public static class Caller {
                public static void Call() { {{receiver}}.Read{{typeArguments}}(new List<string>()); }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var implementationSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(syntax => syntax.Identifier.ValueText == "Read" && syntax.Body != null);
        var implementation = model.GetDeclaredSymbol(implementationSyntax)!;
        var invocationSyntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(syntax => syntax.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Read");
        var invocation = model.GetOperation(invocationSyntax);
        Assert.That(invocation, Is.InstanceOf<IInvocationOperation>());
        var target = ((IInvocationOperation)invocation!).TargetMethod;
        var factory = new IrFactory();
        var result = new ContractBinder(compilation, factory).Bind(target);
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        var contracts = result.Contracts!;
        Assert.That(contracts.Target, Is.SameAs(target));
        TestContext.WriteLine($"SourceOriginalIsImplementation={SymbolEqualityComparer.Default.Equals(contracts.Source.OriginalDefinition, implementation)}; Source={contracts.Source}; Target={target}");
        Assert.That(SymbolEqualityComparer.Default.Equals(contracts.Source.ContainingType,
            target.ContainingType), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(contracts.Source.ReturnType,
            target.ReturnType), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(contracts.Source.Parameters[0].Type,
            target.Parameters[0].Type), Is.True);
        Assert.That(contracts.Source.TypeArguments, Has.Length.EqualTo(target.TypeArguments.Length));
        for (var index = 0; index < target.TypeArguments.Length; index++)
        {
            Assert.That(SymbolEqualityComparer.Default.Equals(contracts.Source.TypeArguments[index],
                target.TypeArguments[index]), Is.True);
        }
        Assert.That(contracts.Clauses, Has.Length.EqualTo(isStatic ? 3 : 4));
        var parameter = contracts.Variables.Single(variable => variable.Role == BoundContractVariableRole.Parameter);
        var returned = contracts.Variables.Single(variable => variable.Role == BoundContractVariableRole.Result);
        var preState = contracts.Variables.Single(variable => variable.Role == BoundContractVariableRole.PreState);
        Assert.That(preState.CurrentStateVariable, Is.EqualTo(parameter.Variable));
        AssertType(parameter.Variable, target.Parameters[0].Type);
        AssertType(preState.Variable, target.Parameters[0].Type);
        AssertType(returned.Variable, target.ReturnType);
        if (!isStatic)
        {
            AssertType(contracts.Variables.Single(variable => variable.Role == BoundContractVariableRole.Receiver).Variable,
                target.ContainingType);
        }
        void AssertType(IrVarId variable, ITypeSymbol expected)
        {
            var expectedType = factory.GetOrCreateReferenceType(
                CompilerIdentityBridge.InternType(factory, expected),
                CompilerIdentityBridge.CreateTypeDisplay(expected));
            Assert.That(factory.GetVariableInfo(variable).Type, Is.EqualTo(expectedType));
        }
    }
}
