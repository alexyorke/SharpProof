using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class OperationPurityScopeCoverageTests
{
    [Test]
    public void ActualOperationBatchCachesTrueAndFalseByIdentityAndDepth()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int x) { var first = Read(x + 1); var second = Read(x++); var third = Unknown(Read(x + 1)); return first + second + third + x; } static int Read(int value) => value; static int Unknown(int value) => value;");
        Assert.That(subject.Invoke([16]), Is.EqualTo(68));
        var invocations = Invocations(subject);
        var pure = invocations.First(operation => operation.Syntax.ToString() == "Read(x + 1)");
        var impure = invocations.Single(operation => operation.Syntax.ToString() == "Read(x++)");
        var unknown = invocations.Single(operation => operation.TargetMethod.Name == "Unknown");
        var calls = new List<string>();
        var classifier = new RoslynOperationPurityClassifier(KnownPure, 256);
        Assert.That(new[]
        {
            classifier.IsDemonstrablyPure(pure, 0),
            classifier.IsDemonstrablyPure(pure, 0),
            classifier.IsDemonstrablyPure(impure, 0),
            classifier.IsDemonstrablyPure(impure, 0),
            classifier.IsDemonstrablyPure(pure, 1),
            classifier.IsDemonstrablyPure(unknown, 0),
            classifier.IsDemonstrablyPure(pure, 256)
        }, Is.EqualTo(new[] { true, true, false, false, true, false, false }));
        Assert.That(calls, Is.EqualTo(new[] { "Read", "Read", "Read", "Unknown" }));

        bool KnownPure(Microsoft.CodeAnalysis.IMethodSymbol method)
        {
            calls.Add(method.Name);
            return method.Name == "Read";
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PublicLoweringClassifiesTheRealCallBoundaryWithoutClaimingExactness(bool increment)
    {
        var body = increment ? "{ var result = Read(x++); return result * 100 + x; }" : "=> Read(x + 1);";
        using var subject = TypedProgramSubject.Create(
            "int Target(int x) " + body + " static int Read(int value) => value;");
        Assert.That(subject.Invoke([16]), Is.EqualTo(increment ? 1617 : 17));
        var invocation = Invocations(subject).Single();
        var lowerer = new RoslynOperationLowerer(subject.Factory, static method => method.Name == "Read");
        var lowered = lowerer.Lower(invocation);
        AssertOpaque(lowered, increment ? IrOpaquePurity.Impure : IrOpaquePurity.Pure);
    }

    [Test]
    public void ReentrantPublicLoweringRestoresTheOuterClassifierScope()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int x) => Outer(Read(x + 1), Read(x + 2)); static int Read(int value) => value; static int Outer(int left, int right) => left + right;");
        Assert.That(subject.Invoke([16]), Is.EqualTo(35));
        var invocations = Invocations(subject);
        var outer = invocations.Single(operation => operation.TargetMethod.Name == "Outer");
        var nested = invocations.Single(operation => operation.Syntax.ToString() == "Read(x + 2)");
        FrontendLoweringResult? nestedResult = null;
        var reentered = false;
        var calls = new List<string>();
        RoslynOperationLowerer? lowerer = null;
        // Reenter while the first argument is being classified. The second
        // argument still needs the restored classifier from the outer scope.
        lowerer = new RoslynOperationLowerer(subject.Factory, KnownPure);
        var result = lowerer.Lower(outer);
        Assert.That(reentered, Is.True);
        Assert.That(nestedResult, Is.Not.Null);
        AssertOpaque(nestedResult!, IrOpaquePurity.Pure);
        AssertOpaque(result, IrOpaquePurity.Pure);
        Assert.That(calls, Is.EqualTo(new[] { "Read", "Read", "Read", "Outer", "Read", "Read" }));
        // The one-shot callback now follows the same policy without reentering.
        // Reuse the lowerer's bindings while requiring a fresh purity scope.
        var control = lowerer.Lower(outer);
        AssertOpaque(control, IrOpaquePurity.Pure);
        Assert.That(result.Term, Is.SameAs(control.Term));
        Assert.That(calls, Is.EqualTo(new[]
        {
            "Read", "Read", "Read", "Outer", "Read", "Read",
            "Read", "Read", "Outer", "Read", "Read"
        }));

        bool KnownPure(Microsoft.CodeAnalysis.IMethodSymbol method)
        {
            calls.Add(method.Name);
            if (method.Name == "Read" && !reentered)
            {
                reentered = true;
                nestedResult = lowerer!.Lower(nested);
            }
            return method.Name is "Read" or "Outer";
        }
    }

    private static IInvocationOperation[] Invocations(TypedProgramSubject subject)
    {
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var model = subject.Compilation.GetSemanticModel(tree);
        return syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(invocation => (IInvocationOperation)model.GetOperation(invocation)!).ToArray();
    }

    private static void AssertOpaque(FrontendLoweringResult result, IrOpaquePurity purity)
    {
        Assert.That(result.Classification.IsExact, Is.False);
        Assert.That(result.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedInvocationShape));
        Assert.That(result.Term, Is.TypeOf<IrOpaqueTerm>());
        Assert.That(((IrOpaqueTerm)result.Term).Purity, Is.EqualTo(purity));
    }
}
