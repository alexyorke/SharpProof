using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class NativeHornApplicationReconstructionTests
{
    [TestCase("and", Z3_decl_kind.Z3_OP_AND, 3, false)]
    [TestCase("or", Z3_decl_kind.Z3_OP_OR, 3, true)]
    [TestCase("not", Z3_decl_kind.Z3_OP_NOT, 1, false)]
    [TestCase("implies", Z3_decl_kind.Z3_OP_IMPLIES, 2, false)]
    [TestCase("equal", Z3_decl_kind.Z3_OP_EQ, 2, false)]
    [TestCase("distinct", Z3_decl_kind.Z3_OP_DISTINCT, 2, true)]
    [TestCase("le", Z3_decl_kind.Z3_OP_LE, 2, false)]
    [TestCase("lt", Z3_decl_kind.Z3_OP_LT, 2, false)]
    [TestCase("ge", Z3_decl_kind.Z3_OP_GE, 2, true)]
    [TestCase("gt", Z3_decl_kind.Z3_OP_GT, 2, true)]
    [TestCase("sle", Z3_decl_kind.Z3_OP_SLEQ, 2, true)]
    [TestCase("slt", Z3_decl_kind.Z3_OP_SLT, 2, true)]
    [TestCase("sge", Z3_decl_kind.Z3_OP_SGEQ, 2, false)]
    [TestCase("sgt", Z3_decl_kind.Z3_OP_SGT, 2, false)]
    [TestCase("ule", Z3_decl_kind.Z3_OP_ULEQ, 2, false)]
    [TestCase("ult", Z3_decl_kind.Z3_OP_ULT, 2, false)]
    [TestCase("uge", Z3_decl_kind.Z3_OP_UGEQ, 2, true)]
    [TestCase("ugt", Z3_decl_kind.Z3_OP_UGT, 2, true)]
    public void RealNativeBooleanApplicationsHaveIndependentlyExpectedTruthValues(
        string operation, Z3_decl_kind expectedKind, int expectedArity, bool expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        var yes = owner.Own(context.MkTrue());
        var no = owner.Own(context.MkFalse());
        var two = owner.Own(context.MkInt(2));
        var five = owner.Own(context.MkInt(5));
        var minusOne = owner.Own(context.MkBV(255UL, 8));
        var one = owner.Own(context.MkBV(1UL, 8));
        var root = owner.Own(operation switch
        {
            "and" => context.MkAnd(yes, no, yes),
            "or" => context.MkOr(no, yes, no),
            "not" => context.MkNot(yes),
            "implies" => context.MkImplies(yes, no),
            "equal" => context.MkEq(two, five),
            "distinct" => context.MkDistinct(two, five),
            "le" => context.MkLe(five, two),
            "lt" => context.MkLt(five, two),
            "ge" => context.MkGe(five, two),
            "gt" => context.MkGt(five, two),
            "sle" => context.MkBVSLE(minusOne, one),
            "slt" => context.MkBVSLT(minusOne, one),
            "sge" => context.MkBVSGE(minusOne, one),
            "sgt" => context.MkBVSGT(minusOne, one),
            "ule" => context.MkBVULE(minusOne, one),
            "ult" => context.MkBVULT(minusOne, one),
            "uge" => context.MkBVUGE(minusOne, one),
            "ugt" => context.MkBVUGT(minusOne, one),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        });
        Expr[] expectedArguments = operation switch
        {
            "and" => [yes, no, yes],
            "or" => [no, yes, no],
            "not" => [yes],
            "implies" => [yes, no],
            "equal" or "distinct" => [two, five],
            "le" or "lt" or "ge" or "gt" => [five, two],
            _ => [minusOne, one]
        };
        AssertApplication(root, expectedKind, expectedArity, expectedArguments);

        Assert.That(DecodeGround(factory, root), Is.SameAs(factory.Boolean(expected)));
    }

    [TestCase("add", Z3_decl_kind.Z3_OP_ADD, 3, 32, 10UL)]
    [TestCase("multiply", Z3_decl_kind.Z3_OP_MUL, 3, 32, 30UL)]
    [TestCase("subtract", Z3_decl_kind.Z3_OP_SUB, 2, 32, 3UL)]
    [TestCase("negate", Z3_decl_kind.Z3_OP_UMINUS, 1, 32, 4294967291UL)]
    [TestCase("bvadd", Z3_decl_kind.Z3_OP_BADD, 2, 8, 0UL)]
    [TestCase("bvmultiply", Z3_decl_kind.Z3_OP_BMUL, 2, 8, 254UL)]
    [TestCase("bvsubtract", Z3_decl_kind.Z3_OP_BSUB, 2, 8, 254UL)]
    [TestCase("bvnegate", Z3_decl_kind.Z3_OP_BNEG, 1, 8, 1UL)]
    [TestCase("ite", Z3_decl_kind.Z3_OP_ITE, 3, 64, 2147483648UL)]
    [TestCase("wideadd", Z3_decl_kind.Z3_OP_ADD, 2, 32, 2147483649UL)]
    [TestCase("nested", Z3_decl_kind.Z3_OP_MUL, 2, 32, 21UL)]
    public void RealNativeArithmeticApplicationsPreserveExpectedBitsAndTypes(
        string operation, Z3_decl_kind expectedKind, int expectedArity, int expectedWidth, ulong expectedBits)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        var two = owner.Own(context.MkInt(2));
        var three = owner.Own(context.MkInt(3));
        var five = owner.Own(context.MkInt(5));
        var one = owner.Own(context.MkInt(1));
        var wide = owner.Own(context.MkInt("2147483648"));
        var minusOne = owner.Own(context.MkBV(255UL, 8));
        var bvOne = owner.Own(context.MkBV(1UL, 8));
        var bvTwo = owner.Own(context.MkBV(2UL, 8));
        var yes = owner.Own(context.MkTrue());
        var sum = owner.Own(context.MkAdd(two, five));
        Expr root = owner.Own<Expr>(operation switch
        {
            "add" => context.MkAdd(two, three, five),
            "multiply" => context.MkMul(two, three, five),
            "subtract" => context.MkSub(five, two),
            "negate" => context.MkUnaryMinus(five),
            "bvadd" => context.MkBVAdd(minusOne, bvOne),
            "bvmultiply" => context.MkBVMul(minusOne, bvTwo),
            "bvsubtract" => context.MkBVSub(minusOne, bvOne),
            "bvnegate" => context.MkBVNeg(minusOne),
            "ite" => context.MkITE(yes, wide, one),
            "wideadd" => context.MkAdd(one, wide),
            "nested" => context.MkMul(sum, three),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        });
        Expr[] expectedArguments = operation switch
        {
            "add" or "multiply" => [two, three, five],
            "subtract" => [five, two],
            "negate" => [five],
            "bvadd" or "bvsubtract" => [minusOne, bvOne],
            "bvmultiply" => [minusOne, bvTwo],
            "bvnegate" => [minusOne],
            "ite" => [yes, wide, one],
            "wideadd" => [one, wide],
            "nested" => [sum, three],
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        AssertApplication(root, expectedKind, expectedArity, expectedArguments);
        var actual = DecodeGround(factory, root);
        Assert.That(actual, Is.TypeOf<IrIntegerTerm>());
        var integer = (IrIntegerTerm)actual!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(integer.Width, Is.EqualTo(expectedWidth));
            Assert.That(integer.Signed, Is.True);
            Assert.That(integer.Bits, Is.EqualTo(expectedBits));
            Assert.That(integer.Type, Is.EqualTo(factory.GetOrCreateIntegerType(expectedWidth, true)));
        }
    }

    [TestCase("oversized-int")]
    [TestCase("unsupported-bv-width")]
    [TestCase("real")]
    public void ARealRejectedNativeChildKeepsItsActualParentRejected(string childKind)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        Expr child = owner.Own<Expr>(childKind switch
        {
            "oversized-int" => context.MkInt("9223372036854775808"),
            "unsupported-bv-width" => context.MkBV(1UL, 128),
            "real" => context.MkReal("1/2"),
            _ => throw new ArgumentOutOfRangeException(nameof(childKind))
        });
        Expr other = owner.Own<Expr>(childKind switch
        {
            "oversized-int" => context.MkInt(0),
            "unsupported-bv-width" => context.MkBV(0UL, 128),
            "real" => context.MkReal("0"),
            _ => throw new ArgumentOutOfRangeException(nameof(childKind))
        });
        var root = owner.Own(context.MkEq(child, other));
        AssertApplication(root, Z3_decl_kind.Z3_OP_EQ, 2, [child, other]);
        Assert.That(SmtNativeUtilities.DecodeIntegerLiteral(factory, child, out _), Is.Null);
        Assert.That(DecodeGround(factory, root), Is.Null);
    }

    [Test]
    public void ARealUnsupportedNativeOperatorKeepsItsActualApplicationRejected()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        var yes = owner.Own(context.MkTrue());
        var no = owner.Own(context.MkFalse());
        var root = owner.Own(context.MkXor(yes, no));
        AssertApplication(root, Z3_decl_kind.Z3_OP_XOR, 2, [yes, no]);
        Assert.That(DecodeGround(factory, root), Is.Null);
    }

    // The only supplied operands are recursively decoded from this very AST's Args.
    // This closed-term component traversal has no state names or caller substitutes.
    private static IrTerm? DecodeGround(IrFactory factory, Expr expression)
    {
        var literal = SmtNativeUtilities.DecodeIntegerLiteral(factory, expression, out var kind);
        if (kind == Z3_decl_kind.Z3_OP_TRUE)
        { return factory.Boolean(true); }
        if (kind == Z3_decl_kind.Z3_OP_FALSE)
        { return factory.Boolean(false); }
        if (literal is not null || kind is Z3_decl_kind.Z3_OP_ANUM or Z3_decl_kind.Z3_OP_BNUM)
        { return literal; }
        var arguments = expression.IsApp ? expression.Args : [];
        try
        {
            IrTerm?[] operands = [.. arguments.Select(argument => DecodeGround(factory, argument))];
            return SmtNativeUtilities.ReconstructHornApplication(factory, expression, operands);
        }
        finally
        {
            foreach (var argument in arguments)
            { argument.Dispose(); }
        }
    }

    private static void AssertApplication(Expr expression, Z3_decl_kind expectedKind, int expectedArity,
        Expr[] expectedArguments)
    {
        using var declaration = expression.FuncDecl;
        var arguments = expression.Args;
        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(expression.IsApp, Is.True);
                Assert.That(declaration.DeclKind, Is.EqualTo(expectedKind));
                Assert.That(expression.NumArgs, Is.EqualTo(expectedArity));
                Assert.That(arguments.Length, Is.EqualTo(expectedArguments.Length));
            }
            for (var index = 0; index < arguments.Length; index++)
            { Assert.That(arguments[index].Equals(expectedArguments[index]), Is.True, $"native child {index}"); }
        }
        finally
        {
            foreach (var argument in arguments)
            { argument.Dispose(); }
        }
    }
}
