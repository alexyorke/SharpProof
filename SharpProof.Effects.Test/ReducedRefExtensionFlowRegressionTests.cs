using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ReducedRefExtensionFlowRegressionTests
{
    [Test]
    public void RefExtensionReceiversAreArgumentsAndInvalidateScalarFacts()
    {
        var mutations = EffectTestHost.EmitReference(
            """
            public static class Mutations {
                public static void SetZero(this ref int value) =>
                    value = 0;
            }
            """,
            "MutationLibrary");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int Divide() {
                    var divisor = 1;
                    divisor.SetZero();
                    return 1 / divisor;
                }
            }
            """,
            mutations);
        var divide = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "Divide");
        var call = Operation(compilation, divide)
            .DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Single();
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.TargetMethod.IsExtensionMethod, Is.True);
            Assert.That(call.TargetMethod.ReducedFrom, Is.Null);
            Assert.That(call.Instance, Is.Null);
            Assert.That(call.Arguments, Has.Length.EqualTo(1));
            Assert.That(
                call.Arguments[0].Parameter?.RefKind,
                Is.EqualTo(RefKind.Ref));
            AssertContainsThrow(
                session.Analyze(divide).Summary,
                "System.DivideByZeroException");
        }
    }

    private static void AssertContainsThrow(
        EffectSummary summary,
        string metadataName)
    {
        var actual = summary.Throws.Types.Select(static type =>
            type.ContainingNamespace.MetadataName + "." +
            type.MetadataName);
        Assert.That(actual, Does.Contain(metadataName));
    }

    private static IOperation Operation(
        Compilation compilation,
        IMethodSymbol method)
    {
        var syntax = method.DeclaringSyntaxReferences.Single().GetSyntax();
        return compilation.GetSemanticModel(syntax.SyntaxTree)
            .GetOperation(syntax) ??
            throw new InvalidOperationException(
                $"Operation for '{method.Name}' was not found.");
    }
}
