using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzFactoryBoundaryTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void StructuralCandidatesDoNotRebuildTermsInADifferentFactory(int shape)
    {
        var source = new IrFactory(IrExecutionSemantics.Total);
        var destination = new IrFactory(IrExecutionSemantics.Total);
        var x = source.Variable(source.CreateVariable("x", source.IntegerType));
        var y = source.Variable(source.CreateVariable("y", source.IntegerType));
        var sum = source.Binary(IrBinaryOperator.Add, x, y);
        var condition = source.Variable(source.CreateVariable("condition", source.BooleanType));
        IrTerm term = shape switch
        {
            0 => source.Unary(IrUnaryOperator.Negate, sum),
            1 => source.Binary(IrBinaryOperator.Multiply, sum, source.Integer(2)),
            _ => source.Conditional(condition, sum, source.Integer(2))
        };

        // This public argument mismatch uses two otherwise ordinary factories.
        // Rebuilds must retain the factory's normal ownership validation.
        var candidates = IrStructuralShrinker.GetCandidates(destination, term);
        var sameFactoryCandidates = IrStructuralShrinker.GetCandidates(source, term);
        Assert.That(candidates, Is.Not.Empty);
        Assert.That(candidates, Is.EqualTo(IrStructuralShrinker.GetCandidates(destination, term)));
        Assert.That(candidates, Is.SubsetOf(sameFactoryCandidates));
        foreach (var candidate in candidates)
        {
            Assert.That(candidate.Type, Is.EqualTo(term.Type));
            Assert.That(IrStructuralShrinker.StructuralSize(candidate),
                Is.LessThan(IrStructuralShrinker.StructuralSize(term)));
            Action sameOwner = () => source.Unary(IrUnaryOperator.Negate, candidate);
            Action differentOwner = () => destination.Unary(IrUnaryOperator.Negate, candidate);
            Assert.DoesNotThrow(sameOwner);
            var error = Assert.Throws<ArgumentException>(differentOwner);
            Assert.That(error!.Message, Does.Contain("different IR factory"));
        }
        Assert.That(candidates, Does.Contain(sum));
    }
}
