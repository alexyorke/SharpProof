using System.Globalization;
using System.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ChildExpressionAdmissionCoverageTests
{
    [TestCase("int Target() => Read()[0]; static int[] Read() => new int[] { 7 };", 7)]
    [TestCase("int Target() => new int[] { 7 }[Index()]; static int Index() => 0;", 7)]
    [TestCase("int Target() { int value = 1; value += Read(); return value; } static int Read() => 2;", 3)]
    public void UnresolvedChildCallStaysClosedAndSourceInliningMatchesCSharp(string members, int expected)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(Convert.ToInt32(subject.Invoke([]), CultureInfo.InvariantCulture), Is.EqualTo(expected));
        var withoutSource = subject.Lower();
        Assert.That(withoutSource.IsExact, Is.False);
        Assert.That(withoutSource.Classification.Abstention, Is.Not.EqualTo(FrontendAbstention.None));
        var withSource = subject.LowerSourceCalls();
        Assert.That(withSource.IsExact, Is.True, withSource.Classification.Abstention.ToString());
        var execution = subject.Execute(withSource, []);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
    }

    [TestCase("bool Target() => Read().Equals(\"proof\"); static string Read() => \"proof\";")]
    [TestCase("bool Target() => \"proof\".Equals(Read()); static string Read() => \"proof\";")]
    public void StringEqualityChildCallsRetainTheReceiverAndArgumentAdmission(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([]), Is.EqualTo(true));
        Assert.That(subject.Lower().IsExact, Is.False);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(declaration => declaration.Identifier.ValueText == "Target");
        var invocation = subject.Compilation.GetSemanticModel(tree).GetOperation(method.ExpressionBody!.Expression)!;
        var clause = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(invocation);
        Assert.That(clause.Classification.IsExact, Is.False);
        Assert.That(clause.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedInvocationShape));
        var withSource = subject.LowerSourceCalls();
        Assert.That(withSource.IsExact, Is.True, withSource.Classification.Abstention.ToString());
        var execution = subject.Execute(withSource, []);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.Boolean, Is.True);
    }

    [TestCase("bool Target() => Read() && true; static bool Read() => false;", "bool Target() => false && true;")]
    [TestCase("int Target() => Read() ? 7 : 9; static bool Read() => false;", "int Target() => false ? 7 : 9;")]
    public void DirectBodyConditionalAdmissionDoesNotHideAnUnsupportedChild(string source, string control)
    {
        using var subject = TypedProgramSubject.Create(source);
        using var supported = TypedProgramSubject.Create(control);
        Assert.That(subject.Invoke([]), Is.EqualTo(supported.Invoke([])));
        var rejected = LowerDirectBody(subject);
        Assert.That(rejected.Classification.IsExact, Is.False);
        Assert.That(rejected.Classification.Abstention, Is.Not.EqualTo(FrontendAbstention.None));
        Assert.That(LowerDirectBody(supported).Classification.IsExact, Is.True);
    }

    [TestCase("bool Target() => \"proof\".Equals(\"proof\");", true)]
    [TestCase("bool Target() => \"proof\".Equals(\"other\");", false)]
    public void SupportedStringEqualityStillMatchesCompiledCSharp(string members, bool expected)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([]), Is.EqualTo(expected));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        var execution = subject.Execute(lowering, []);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.Boolean, Is.EqualTo(expected));
    }

    private static TotalBodyValue LowerDirectBody(TypedProgramSubject subject)
    {
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(declaration => declaration.Identifier.ValueText == "Target");
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(method.ExpressionBody!.Expression)!;
        var builder = new IrProgramBuilder(subject.Factory);
        var entry = builder.CreateBlock("entry");
        builder.SetEntry(entry);
        return new RoslynTotalExpressionLowerer(subject.Context, builder).LowerBodyValue(operation, entry);
    }
}
