using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractInvocationShapeTests
{
    [TestCase("Zero", 0)]
    [TestCase("One", 1)]
    [TestCase("Two", 2)]
    public void UnaryApplicationUsesTheActualOwnedInvocationShape(string name, int argumentCount)
    {
        var compilation = TestCompilation.Create("InvocationShape", """
            public static class Calls {
                public static bool Zero() => true;
                public static bool One(bool value) => value;
                public static bool Two(bool left, bool right) => left && right;
                public static void Caller() { Zero(); One(true); Two(true, false); }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(call => call.Expression.ToString() == name);
        var invocation = (IInvocationOperation)model.GetOperation(syntax)!;
        Assert.That(invocation.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(invocation.TargetMethod.Name, Is.EqualTo(name));
        Assert.That(invocation.TargetMethod.Parameters, Has.Length.EqualTo(argumentCount));
        Assert.That(invocation.Arguments, Has.Length.EqualTo(argumentCount));
        var applied = ContractInvocationShape.ApplySingleArgument(
            invocation, model,
            static (call, ownedModel) => ownedModel.GetOperation(call.Syntax),
            (IOperation?)null);
        if (argumentCount != 1)
        {
            Assert.That(applied, Is.Null);
            return;
        }
        Assert.That(applied, Is.InstanceOf<IInvocationOperation>());
        Assert.That(applied!.Syntax, Is.SameAs(syntax));
        Assert.That(applied.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(SymbolEqualityComparer.Default.Equals(
            ((IInvocationOperation)applied).TargetMethod, invocation.TargetMethod), Is.True);
        var value = ((IInvocationOperation)applied).Arguments.Single().Value;
        Assert.That(value.ConstantValue.HasValue, Is.True);
        Assert.That(value.ConstantValue.Value, Is.EqualTo(true));
    }
}
