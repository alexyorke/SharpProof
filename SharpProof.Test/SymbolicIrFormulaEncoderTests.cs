using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

[TestFixture]
internal sealed class SymbolicIrFormulaEncoderTests {
    [Test]
    public void ConditionalElementReceivers_EncodeAsDistinctFormulas() {
        var whenTrue = new SymbolicVariableTerm("a", SmtValueKind.Reference);
        var whenFalse = new SymbolicVariableTerm("b", SmtValueKind.Reference);
        var first = new SymbolicElementTerm(
            new SymbolicConditionalTerm(new SymbolicConstantCondition(true), whenTrue, whenFalse),
            new SymbolicIntegerConstantTerm(0),
            SmtValueKind.Int);
        var second = new SymbolicElementTerm(
            new SymbolicConditionalTerm(new SymbolicConstantCondition(false), whenTrue, whenFalse),
            new SymbolicIntegerConstantTerm(0),
            SmtValueKind.Int);

        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(first, out var firstFormula), Is.True);
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(second, out var secondFormula), Is.True);
        Assert.That(firstFormula, Is.Not.EqualTo(secondFormula));
    }
}
