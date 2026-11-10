using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class FrontendPublicLoweringBoundaryTests
{
    [Test]
    public void TotalContextRequiresTotalFactorySemantics()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) => x;");
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            _ = new TotalLoweringContext(new IrFactory(), subject.Context.Target)));
        Assert.That(error!.ParamName, Is.EqualTo("factory"));
    }

    [Test]
    public void EntryVariablesMatchTheActualCallableInputs()
    {
        using var subject = TypedProgramSubject.Create("int Target(int left, int right) => left + right;");
        Assert.That(subject.Context.EntryVariables,
            Is.EqualTo(subject.Context.Inputs.Select(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Entry))));
        Assert.That(subject.Context.EntryVariables, Has.Length.EqualTo(2));
    }

    [TestCase("int Target(ref int x) { ref int alias = ref (x); return alias; }")]
    [TestCase("int Target(ref int x) => Read(ref (x)); static int Read(ref int value) => value;")]
    public void ParenthesizedReferenceUsesRemainOutsideReadOnlyParameterAdmission(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        using var subject = TypedProgramSubject.Create(source);
        Assert.That(subject.Invoke([7]), Is.EqualTo(7));
        Assert.That(subject.Context.HasScalarSignature, Is.False);
        Assert.That(subject.Lower().IsExact, Is.False);
    }
}
