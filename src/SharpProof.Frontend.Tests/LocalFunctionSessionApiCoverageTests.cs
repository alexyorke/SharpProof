using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class LocalFunctionSessionApiCoverageTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void GenuineLocalFunctionGraphPreparationKeepsCaptureAdmission(bool isStatic, bool captures)
    {
        var modifier = isStatic ? "static " : "";
        var expression = captures ? "value + item" : "item + 1";
        using var subject = TypedProgramSubject.Create(
            "int Target(int value) { " + modifier + "int Helper(int item) => " + expression + "; return Helper(1); }");
        Assert.That(subject.Invoke([8]), Is.EqualTo(captures ? 9 : 2));
        var tree = subject.Compilation.SyntaxTrees.Single();
        Assert.That(subject.Compilation.References.OfType<CompilationReference>(), Is.Empty);
        var model = subject.Compilation.GetSemanticModel(tree);
        var local = tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(syntax)!;
        Assert.That(invocation.TargetMethod.MethodKind, Is.EqualTo(MethodKind.LocalFunction));
        Assert.That(invocation.TargetMethod.IsStatic, Is.EqualTo(isStatic));
        Assert.That(invocation.Instance, Is.Null);
        Assert.That(invocation.Arguments, Has.Length.EqualTo(1));
        Assert.That(invocation.Arguments[0].ArgumentKind, Is.EqualTo(ArgumentKind.Explicit));
        Assert.That(invocation.Arguments[0].Value.ConstantValue.Value, Is.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(local), invocation.TargetMethod), Is.True);
        var localGraph = TotalSourceCallSession.CreateGraph(local, invocation.TargetMethod, model, default);
        Assert.That(localGraph == null, Is.EqualTo(captures));
        var preparations = 0;
        bool PrepareCallee(TotalLoweringContext candidate)
        {
            System.ArgumentNullException.ThrowIfNull(candidate);
            preparations++;
            Assert.That(captures, Is.False);
            Assert.That(localGraph, Is.Not.Null);
            Assert.That(candidate.Factory, Is.SameAs(subject.Factory));
            Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target, invocation.TargetMethod), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target.ContainingAssembly,
                subject.Compilation.Assembly), Is.True);
            Assert.That(candidate.Compilation, Is.Null);
            Assert.That(localGraph!.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
            var prepared = new RoslynProgramLowerer(subject.Factory).LowerCandidate(localGraph, candidate);
            Assert.That(candidate.Compilation, Is.SameAs(subject.Compilation));
            Assert.That(prepared.IsExact, Is.True);
            Assert.That(prepared.ConstructionLimitExceeded, Is.False);
            return prepared.IsExact;
        }
        var session = new TotalSourceCallSession(subject.Compilation, PrepareCallee, null, default);
        Assert.That(session.TryPrepare(subject.Context, invocation.TargetMethod, invocation.Instance,
            invocation.Arguments, out var frame, out var graph), Is.EqualTo(!captures));
        Assert.That(preparations, Is.EqualTo(captures ? 0 : 1));
        Assert.That(frame == null, Is.EqualTo(captures));
        Assert.That(graph == null, Is.EqualTo(captures));
        Assert.That(session.ConstructionLimitExceeded, Is.False);
        Assert.That(session.IsActive(invocation.TargetMethod), Is.False);
        if (!captures)
        {
            Assert.That(SymbolEqualityComparer.Default.Equals(frame!.Target, invocation.TargetMethod), Is.True);
            Assert.That(graph!.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
            var lowering = new RoslynTotalProgramLowerer(subject.Context, default, session).Lower(subject.Graph);
            Assert.That(lowering.IsExact, Is.True);
            Assert.That(lowering.ConstructionLimitExceeded, Is.False);
            var replay = subject.Execute(lowering, [8]);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(2)));
        }
    }
}
