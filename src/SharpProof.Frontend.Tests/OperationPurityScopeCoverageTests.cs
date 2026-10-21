using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class OperationPurityScopeCoverageTests
{
    private static readonly ImmutableArray<bool> ExpectedBatchResults = [true, true, false, false, true, false, false];
    private static readonly ImmutableArray<string> ExpectedBatchCalls = ["Read", "Read", "Read", "Unknown"];
    private static readonly ImmutableArray<string> ExpectedReentryCalls = ["Read", "Read", "Read", "Outer", "Read", "Read"];
    private static readonly ImmutableArray<string> ExpectedBothScopeCalls =
    [
        "Read", "Read", "Read", "Outer", "Read", "Read",
        "Read", "Read", "Outer", "Read", "Read"
    ];

    [Test]
    public void ActualOperationBatchCachesTrueAndFalseByIdentityAndDepth()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int x) { var first = Read(x + 1); var second = Read(x++); var third = Unknown(Read(x + 1)); return first + second + third + x; } static int Read(int value) => value; static int Unknown(int value) => value;");
        Assert.That(subject.Invoke([16]), Is.EqualTo(68));
        var invocations = Invocations(subject);
        var pure = invocations.First(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.Syntax.ToString() == "Read(x + 1)"));
        var impure = invocations.Single(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.Syntax.ToString() == "Read(x++)"));
        var unknown = invocations.Single(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.TargetMethod.Name == "Unknown"));
        var calls = ImmutableArray.CreateBuilder<string>();
        var classifier = new RoslynOperationPurityClassifier(KnownPure, 256);
        Assert.That(ImmutableArray.Create(
            classifier.IsDemonstrablyPure(pure, 0),
            classifier.IsDemonstrablyPure(pure, 0),
            classifier.IsDemonstrablyPure(impure, 0),
            classifier.IsDemonstrablyPure(impure, 0),
            classifier.IsDemonstrablyPure(pure, 1),
            classifier.IsDemonstrablyPure(unknown, 0),
            classifier.IsDemonstrablyPure(pure, 256)
        ), Is.EqualTo(ExpectedBatchResults));
        Assert.That(calls.ToImmutable(), Is.EqualTo(ExpectedBatchCalls));

        bool KnownPure(Microsoft.CodeAnalysis.IMethodSymbol method)
        {
            System.ArgumentNullException.ThrowIfNull(method);
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
        var lowerer = new RoslynOperationLowerer(subject.Factory, static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Name == "Read"));
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
        var outer = invocations.Single(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.TargetMethod.Name == "Outer"));
        var nested = invocations.Single(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.Syntax.ToString() == "Read(x + 2)"));
        FrontendLoweringResult? nestedResult = null;
        var reentered = false;
        var calls = ImmutableArray.CreateBuilder<string>();
        RoslynOperationLowerer? lowerer = null;
        // Reenter while the first argument is being classified. The second
        // argument still needs the restored classifier from the outer scope.
        lowerer = new RoslynOperationLowerer(subject.Factory, KnownPure);
        var result = lowerer.Lower(outer);
        Assert.That(reentered, Is.True);
        Assert.That(nestedResult, Is.Not.Null);
        AssertOpaque(nestedResult!, IrOpaquePurity.Pure);
        AssertOpaque(result, IrOpaquePurity.Pure);
        Assert.That(calls.ToImmutable(), Is.EqualTo(ExpectedReentryCalls));
        // The one-shot callback now follows the same policy without reentering.
        // Reuse the lowerer's bindings while requiring a fresh purity scope.
        var control = lowerer.Lower(outer);
        AssertOpaque(control, IrOpaquePurity.Pure);
        Assert.That(result.Term, Is.SameAs(control.Term));
        Assert.That(calls.ToImmutable(), Is.EqualTo(ExpectedBothScopeCalls));

        bool KnownPure(Microsoft.CodeAnalysis.IMethodSymbol method)
        {
            System.ArgumentNullException.ThrowIfNull(method);
            calls.Add(method.Name);
            if (method.Name == "Read" && !reentered)
            {
                reentered = true;
                nestedResult = lowerer!.Lower(nested);
            }
            return method.Name is "Read" or "Outer";
        }
    }

    private static ImmutableArray<IInvocationOperation> Invocations(TypedProgramSubject subject)
    {
        System.ArgumentNullException.ThrowIfNull(subject);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var model = subject.Compilation.GetSemanticModel(tree);
        return syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : ((IInvocationOperation)model.GetOperation(invocation)!)).ToImmutableArray();
    }

    private static void AssertOpaque(FrontendLoweringResult result, IrOpaquePurity purity)
    {
        System.ArgumentNullException.ThrowIfNull(result);
        Assert.That(result.Classification.IsExact, Is.False);
        Assert.That(result.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedInvocationShape));
        Assert.That(result.Term, Is.TypeOf<IrOpaqueTerm>());
        Assert.That(((IrOpaqueTerm)result.Term).Purity, Is.EqualTo(purity));
    }
}
