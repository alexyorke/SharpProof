using Microsoft.Z3;
using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class NativeBooleanEqualityDispatchTests
{
    [Test]
    public void BooleanEqualityAndEquivalenceShareTheNativeEqualityDeclaration()
    {
        using var context = new Context();
        using var left = context.MkBoolConst("left");
        using var right = context.MkBoolConst("right");
        using var equality = context.MkEq(left, right);
        using var equivalence = context.MkIff(left, right);
        using var equalityDeclaration = equality.FuncDecl;
        using var equivalenceDeclaration = equivalence.FuncDecl;
        Assert.That(equalityDeclaration.DeclKind, Is.EqualTo(Z3_decl_kind.Z3_OP_EQ));
        Assert.That(equivalenceDeclaration.DeclKind, Is.EqualTo(Z3_decl_kind.Z3_OP_EQ));
        using var difference = context.MkXor(equality, equivalence);
        using var solver = context.MkSolver();
        solver.Assert(difference);
        Assert.That(solver.Check(), Is.EqualTo(Status.UNSATISFIABLE));
    }

    [Test]
    public async Task BooleanEqualitySymmetryIsProvenThroughThePublicBackend()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var left = factory.Variable(factory.CreateVariable("left", factory.BooleanType));
        var right = factory.Variable(factory.CreateVariable("right", factory.BooleanType));
        var equality = factory.Binary(IrBinaryOperator.Equal, left, right);
        var symmetry = factory.Binary(IrBinaryOperator.Equal, right, left);
        var query = new VerificationQuery(factory,
            [new Assumption(factory, equality, new LoweredJustification(factory.CreateOperation()))],
            new Goal(factory, symmetry, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(backend);
        Assert.That(await kernel.VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
    }
}
