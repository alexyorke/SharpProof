using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceStringIndexFaultCoverageTests
{
    [TestCase(null, 7)]
    [TestCase("", 9)]
    public void SourceReceiverCallPreservesDocumentedLastCharacterFaults(string? input, int expected)
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(string text) { try { _ = Read(text)[^1]; return 0; } " +
            "catch (System.NullReferenceException) { return 7; } " +
            "catch (System.IndexOutOfRangeException) { return 9; } } " +
            "static string Read(string value) => value;");
        object[] arguments = [input!];
        Assert.That(subject.Invoke(arguments), Is.EqualTo(expected));
        var withoutSource = subject.Lower();
        Assert.That(withoutSource.IsExact, Is.False);
        Assert.That(withoutSource.Classification.Abstention, Is.Not.EqualTo(FrontendAbstention.None));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        var execution = subject.Execute(lowering, arguments);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(expected));
    }
}
