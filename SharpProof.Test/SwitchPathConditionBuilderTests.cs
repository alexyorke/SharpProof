using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

[TestFixture]
internal sealed class SwitchPathConditionBuilderTests {
    private static readonly SymbolicVariableTerm Designation = new("designation", SmtValueKind.Reference);
    private static readonly SymbolicVariableTerm GoverningValue = new("governing", SmtValueKind.Reference);

    [Test]
    public void DesignationGuardSubstitution_RewritesStringSliceReceiver() {
        var slice = new SymbolicStringSliceTerm(
            new SymbolicStringContentTerm(Designation),
            new SymbolicIntegerConstantTerm(1),
            new SymbolicIntegerConstantTerm(2));
        var rewritten = Rewrite(new SymbolicRelationAtom(
            SymbolicRelationOperator.Equal, slice, new SymbolicStringConstantTerm("bc")));
        var relation = (SymbolicRelationAtom)rewritten.Fact.Atom;
        Assert.That(((SymbolicStringContentTerm)((SymbolicStringSliceTerm)relation.Left).Value).Reference,
            Is.EqualTo(GoverningValue));
    }

    [Test]
    public void DesignationGuardSubstitution_PreservesExceptionPrecondition() {
        var rewritten = Rewrite(new SymbolicExceptionPreconditionAtom(
            SymbolicExceptionPreconditionKind.NullDereference,
            Designation,
            Fact(new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual, Designation, new SymbolicNullTerm()))));
        var precondition = (SymbolicExceptionPreconditionAtom)rewritten.Fact.Atom;
        var trigger = (SymbolicRelationAtom)((SymbolicFactCondition)precondition.Trigger).Fact.Atom;
        Assert.Multiple(() => {
            Assert.That(precondition.Subject, Is.EqualTo(GoverningValue));
            Assert.That(trigger.Left, Is.EqualTo(GoverningValue));
        });
    }

    [Test]
    public void DesignationGuardSubstitution_PreservesMayOverflowMetadata() {
        var binary = new SymbolicBinaryTerm(
            SymbolicBinaryTermOperator.Add, Designation, new SymbolicIntegerConstantTerm(1), MayOverflow: true);
        var rewritten = Rewrite(new SymbolicRelationAtom(
            SymbolicRelationOperator.GreaterThan, binary, new SymbolicIntegerConstantTerm(0)));
        var result = (SymbolicBinaryTerm)((SymbolicRelationAtom)rewritten.Fact.Atom).Left;
        Assert.Multiple(() => {
            Assert.That(result.Left, Is.EqualTo(GoverningValue));
            Assert.That(result.MayOverflow, Is.True);
        });
    }

    private static SymbolicFactCondition Rewrite(SymbolicAtom atom) =>
        (SymbolicFactCondition)SymbolicIrSubstitution.ReplaceVariableNames(
            Fact(atom),
            new Dictionary<string, SymbolicTerm>(StringComparer.Ordinal) { [Designation.Name] = GoverningValue });
    private static SymbolicFactCondition Fact(SymbolicAtom atom) => new(new SymbolicFact(
        atom, true, SymbolicFactConfidence.Exact, "test.switch.guard", default, null, null));
}
