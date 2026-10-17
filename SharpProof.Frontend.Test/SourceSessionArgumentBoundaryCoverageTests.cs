using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceSessionArgumentBoundaryCoverageTests
{
    [Test]
    public void GenuineCalleeArgumentsPrepareAndDuplicateOrdinalApiInputIsRejected()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int left, int right) => Helper(left, right); static int Helper(int left, int right) => left + right;");
        Assert.That(subject.Invoke([7, 3]), Is.EqualTo(10));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(invocation.Instance, Is.Null);
        Assert.That(invocation.Arguments, Has.Length.EqualTo(2));
        Assert.That(invocation.Arguments[0].Parameter!.Ordinal, Is.Zero);
        Assert.That(invocation.Arguments[1].Parameter!.Ordinal, Is.EqualTo(1));
        foreach (var argument in invocation.Arguments)
        {
            Assert.That(argument.ArgumentKind, Is.EqualTo(ArgumentKind.Explicit));
            Assert.That(SymbolEqualityComparer.Default.Equals(argument.Parameter!.ContainingSymbol,
                invocation.TargetMethod), Is.True);
        }
        var helperSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Helper"));
        var helperGraph = ControlFlowGraph.Create(helperSyntax, subject.Compilation.GetSemanticModel(tree))!;
        bool PrepareCallee(TotalLoweringContext candidate)
        {
            System.ArgumentNullException.ThrowIfNull(candidate);
            Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target, invocation.TargetMethod), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target.ContainingAssembly,
                subject.Compilation.Assembly), Is.True);
            Assert.That(candidate.Factory, Is.SameAs(subject.Factory));
            Assert.That(helperGraph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
            var prepared = new RoslynProgramLowerer(subject.Factory).LowerCandidate(helperGraph, candidate);
            Assert.That(prepared.IsExact, Is.True);
            Assert.That(candidate.Compilation, Is.SameAs(subject.Compilation));
            return prepared.IsExact;
        }
        var session = new TotalSourceCallSession(subject.Compilation, PrepareCallee, null, default);
        Assert.That(session.TryPrepare(subject.Context, invocation.TargetMethod, invocation.Instance,
            invocation.Arguments, out var frame, out var graph), Is.True);
        Assert.That(frame, Is.Not.Null);
        Assert.That(graph, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(frame!.Target, invocation.TargetMethod), Is.True);
        // Deliberately invalid API collection: two references to the same real
        // compiler argument. It must return no executable frame or graph.
        Assert.That(session.TryPrepare(subject.Context, invocation.TargetMethod, invocation.Instance,
            [invocation.Arguments[0], invocation.Arguments[0]], out var rejectedFrame, out var rejectedGraph), Is.False);
        Assert.That(rejectedFrame, Is.Null);
        Assert.That(rejectedGraph, Is.Null);
        Assert.That(session.ConstructionLimitExceeded, Is.False);
        Assert.That(session.IsActive(invocation.TargetMethod), Is.False);
        var lowering = new RoslynTotalProgramLowerer(subject.Context, default, session).Lower(subject.Graph);
        Assert.That(lowering.IsExact, Is.True);
        var replay = subject.Execute(lowering, [7, 3]);
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(10)));
    }
}
