using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

// The IL lowering tables give a meaning only to the opcodes they name. An
// opcode outside them must abstain rather than be read as some other one.
[TestFixture]
public sealed class IlOpcodeTableFailClosedTests
{
    [TestCase("Bogus")]
    [TestCase("Cne")]
    [TestCase("Br")]
    public void UnknownComparisonOpcodeAbstains(string code)
    {
        var factory = new IrFactory();
        Assert.Throws<RoslynTotalProgramLowerer.RegionIncompleteException>(
            (System.Action)(() => _ = RoslynTotalProgramLowerer.IlComparison(factory, code, factory.Integer(1), factory.Integer(2))));
    }

    [TestCase("Bogus")]
    [TestCase("Addx")]
    [TestCase("Neg")]
    public void UnknownArithmeticOpcodeAbstains(string code)
    {
        var factory = new IrFactory();
        Assert.Throws<RoslynTotalProgramLowerer.RegionIncompleteException>(
            (System.Action)(() => _ = RoslynTotalProgramLowerer.IlArithmetic(factory, code, factory.Integer(1), factory.Integer(2))));
    }
}
