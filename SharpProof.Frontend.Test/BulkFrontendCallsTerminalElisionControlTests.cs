using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkFrontendCallsTerminalElisionControlTests
{
    private const string OmittedLog =
        "[System.Diagnostics.Conditional(\"ABSENT\")] static void Log(bool argument) { }";
    private const string TerminalArgument =
        "value > 0 ? throw new System.NullReferenceException() : throw new System.InvalidOperationException()";

    [TestCase(-1, 4)]
    [TestCase(0, 6)]
    [TestCase(3, 12)]
    public void RuntimePrefixAndFollowingMutationRemainOwned(int input, int expected)
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int value) { int result = value; result += 3; Log(" + TerminalArgument +
            "); result *= 2; return result; } " + OmittedLog);
        AssertRuntimeAssignmentsRemainOwned(subject);
        AssertReplay(subject, input, expected);
    }

    [TestCase(-1, 4)]
    [TestCase(0, 5)]
    [TestCase(3, 8)]
    public void UnrelatedConstantDeadMutationsStayDead(int input, int expected)
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int value) { int result = value; if (false) { result = 99; System.Console.WriteLine(\"dead\"); } Log(" + TerminalArgument +
            "); result += 5; if (false) { result = 88; } return result; } " + OmittedLog);
        AssertRuntimeAssignmentsRemainOwned(subject);
        AssertReplay(subject, input, expected);
    }

    [TestCase(-1, 9)]
    [TestCase(0, 12)]
    [TestCase(3, 21)]
    public void FinallyRunsAfterTheRecoveredOmittedArgumentContinuation(int input, int expected)
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int value) { int result = value; try { Log(" + TerminalArgument +
            "); result += 4; } finally { result *= 3; } return result; } " + OmittedLog);
        AssertRuntimeAssignmentsRemainOwned(subject);
        AssertReplay(subject, input, expected);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void NestedCallableBodiesDoNotAcquireTheOmittedOwnersClassification(bool localFunction, bool input)
    {
        const string terminal = "flag ? throw new System.NullReferenceException() : throw new System.InvalidOperationException()";
        var argument = localFunction
            ? "() => { bool Local() => " + terminal + "; return Local(); }"
            : "() => " + terminal;
        using var subject = TypedProgramSubject.Create(
            "int Target(bool flag) { Log(" + argument + "); return 7; } " +
            "[System.Diagnostics.Conditional(\"ABSENT\")] static void Log(System.Func<bool> argument) { }");
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var emission = new CSharpInvocationEmissionPolicy(subject.Compilation);
        var throws = tree.GetRoot().DescendantNodes().OfType<ThrowExpressionSyntax>().ToArray();
        Assert.That(throws, Has.Length.EqualTo(2));
        foreach (var syntax in throws)
        {
            var operation = model.GetOperation(syntax);
            Assert.That(operation, Is.InstanceOf<IThrowOperation>());
            Assert.That(emission.IsElided(operation!), Is.False);
        }
        AssertReplay(subject, input, 7);
    }

    private static void AssertRuntimeAssignmentsRemainOwned(TypedProgramSubject subject)
    {
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var assignments = tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>().ToArray();
        Assert.That(assignments, Is.Not.Empty);
        var emission = new CSharpInvocationEmissionPolicy(subject.Compilation);
        foreach (var syntax in assignments)
        {
            var operation = model.GetOperation(syntax);
            Assert.That(operation, Is.Not.Null);
            Assert.That(emission.IsElided(operation!), Is.False);
        }
    }

    private static void AssertReplay(TypedProgramSubject subject, object input, int expected)
    {
        // TypedProgramSubject also checks Emit and loads the emitted CLR image.
        Assert.That(subject.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(subject.Context.OwnsBody(subject.Graph.OriginalOperation), Is.True);
        Assert.That(subject.Graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var ownerSyntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "Log" });
        var owner = (IInvocationOperation)model.GetOperation(ownerSyntax)!;
        Assert.That(owner.TargetMethod.IsConditional, Is.True);
        Assert.That(new CSharpInvocationEmissionPolicy(subject.Compilation).IsElided(owner), Is.True);
        Assert.That(subject.Invoke([input]), Is.EqualTo(expected));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowering, [input]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }
}
