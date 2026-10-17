using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ParenthesizedDelegateErasureCoverageTests
{
    [Test]
    public void ParenthesizedDiscardRetainsCompiledResultAndRequiresItsCompilationForErasure()
    {
        using var subject = TypedProgramSubject.Create("int Target() { _ = ((new System.Action(Sink))); return 17; } static void Sink() { }");
        Assert.That(subject.Invoke([]), Is.EqualTo(17));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (syntax.Identifier.ValueText == "Target"));
        var body = subject.Compilation.GetSemanticModel(tree).GetOperation(method)!;
        var creation = body.DescendantsAndSelf().OfType<IDelegateCreationOperation>().Single();
        Assert.That(CSharpOperationSemantics.DelegateValueIsErased(creation, null), Is.False);
        Assert.That(CSharpOperationSemantics.DelegateValueIsErased(creation, subject.Compilation), Is.True);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, []);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
    }
}
