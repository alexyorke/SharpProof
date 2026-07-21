using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

[TestFixture]
public sealed class SmtFormulaVersionRewriterTests {
    [Test]
    public void RewriteSymbolVersions_DoesNotRewritePrefixSiblingVariableName() {
        var source = new SymbolicVariableTerm("myField", SmtValueKind.Int);
        var sibling = new SymbolicVariableTerm("myFieldB", SmtValueKind.Int);

        var rewritten = SymbolicIrSubstitution.ReplaceTerm(
            sibling,
            source,
            new SymbolicVariableTerm("myField@v1", SmtValueKind.Int));

        Assert.That(rewritten, Is.EqualTo(sibling));
    }

    [Test]
    public void RewriteSymbolVersions_RewritesElementAccessForTargetVariable() {
        var source = new SymbolicVariableTerm("myField", SmtValueKind.Reference);
        var element = new SymbolicElementTerm(
            source,
            new SymbolicIntegerConstantTerm(0),
            SmtValueKind.Int);
        var versioned = new SymbolicVariableTerm("myField@v1", SmtValueKind.Reference);

        var rewritten = SymbolicIrSubstitution.ReplaceTerm(element, source, versioned);

        Assert.That( rewritten, Is.EqualTo(new SymbolicElementTerm( versioned, new SymbolicIntegerConstantTerm(0), SmtValueKind.Int)));
    }
}
