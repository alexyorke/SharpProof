using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ParamsArrayNaturalBudgetApiCoverageTests
{
    [Test]
    public void ActualParamsInvocationsConsumeTheDefaultArrayModelScanBudget()
    {
        var callCount = RoslynTotalProgramLowerer.MaximumRegionSteps / 2;
        var callsSource = string.Join("\n", Enumerable.Repeat("sum += Helper();", callCount));
        var source = "int Target() { int sum = 0; " + callsSource +
            " return sum; } static int Helper(params int[] values) => values.Length + 1;";
        using var subject = TypedProgramSubject.Create(source);
        Assert.That(subject.Invoke([]), Is.EqualTo(callCount));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var target = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var calls = target.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : ((IInvocationOperation)model.GetOperation(syntax)!)).ToArray();
        Assert.That(calls, Has.Length.EqualTo(callCount));
        var session = new TotalSourceCallSession(subject.Compilation, null, null, default,
            method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (ResolveArrayEmpty(subject.Factory, method)));
        Assert.That(session.Enter(subject.Context.Target), Is.True);
        try
        {
            for (var ordinal = 0; ordinal < calls.Length; ordinal++)
            {
                var invocation = calls[ordinal];
                Assert.That(invocation.Arguments, Has.Length.EqualTo(1));
                Assert.That(SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingAssembly,
                    subject.Compilation.Assembly), Is.True);
                Assert.That(SymbolEqualityComparer.Default.Equals(invocation.Arguments[0].Parameter!.ContainingSymbol,
                    invocation.TargetMethod), Is.True);
                Assert.That(TotalSourceCallSession.IsEmptyParamsArray(invocation.Arguments[0]), Is.True);
                var prepared = session.PrepareEmptyParamsArray(invocation.Arguments[0]);
                Assert.That(prepared.HasValue, Is.EqualTo(ordinal < calls.Length - 1));
            }
            Assert.That(session.ConstructionLimitExceeded, Is.True);
            Assert.That(session.IsActive(subject.Context.Target), Is.True);
        }
        finally
        {
            session.Leave(subject.Context.Target);
        }
        Assert.That(session.IsActive(subject.Context.Target), Is.False);

        using var control = TypedProgramSubject.Create(
            "int Target() => Helper(); static int Helper(params int[] values) => values.Length + 1;");
        Assert.That(control.Invoke([]), Is.EqualTo(1));
        var controlTree = control.Compilation.SyntaxTrees.Single();
        var controlModel = control.Compilation.GetSemanticModel(controlTree);
        var helperSyntax = controlTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Helper"));
        var helperSymbol = controlModel.GetDeclaredSymbol(helperSyntax)!;
        var helperGraph = ControlFlowGraph.Create(helperSyntax, controlModel)!;
        bool PrepareCallee(TotalLoweringContext candidate)
        {
            System.ArgumentNullException.ThrowIfNull(candidate);
            Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target, helperSymbol), Is.True);
            Assert.That(candidate.Factory, Is.SameAs(control.Factory));
            Assert.That(helperGraph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(control.Compilation));
            var prepared = new RoslynProgramLowerer(control.Factory).LowerCandidate(helperGraph, candidate);
            Assert.That(candidate.Compilation, Is.SameAs(control.Compilation));
            Assert.That(prepared.IsExact, Is.True);
            return prepared.IsExact;
        }
        var supported = new TotalSourceCallSession(control.Compilation, PrepareCallee, null, default,
            method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (ResolveArrayEmpty(control.Factory, method)));
        var program = new RoslynTotalProgramLowerer(control.Context, default, supported).Lower(control.Graph);
        Assert.That(program.IsExact, Is.True);
        Assert.That(program.ConstructionLimitExceeded, Is.False);
        Assert.That(supported.ConstructionLimitExceeded, Is.False);
        var replay = control.Execute(program, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.One));
    }

    private static TotalScalarCallModel ResolveArrayEmpty(IrFactory factory, IMethodSymbol method)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(method);
        Assert.That(method.ContainingType.SpecialType, Is.EqualTo(SpecialType.System_Array));
        Assert.That(method.Name, Is.EqualTo("Empty"));
        Assert.That(method.Parameters, Is.Empty);
        Assert.That(method.TypeArguments, Has.Length.EqualTo(1));
        Assert.That(method.TypeArguments[0].SpecialType, Is.EqualTo(SpecialType.System_Int32));
        return new(method.Parameters.Length, _ => CSharpOperationSemantics.ArrayEmpty(factory,
            new RoslynTypeMapper(factory).GetTypeId(method.ReturnType)));
    }
}
