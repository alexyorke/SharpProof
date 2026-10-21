using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class DeferredStringConcatenationCoverageTests
{
    [TestCase("proof", true, 6)]
    [TestCase("proof", false, 7)]
    [TestCase(null, true, 1)]
    [TestCase(null, false, 2)]
    public void DeferredSingleOperandConcatenationMatchesBothCompiledBranches(string? input, bool choose, int expected)
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(string text, bool choose) => ((text + \"\") + (choose ? \"a\" : \"bb\")).Length;");
        Assert.That(subject.Invoke([input!, choose]), Is.EqualTo(expected));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        var inputs = ImmutableDictionaryCapture.Create(subject.Context.Parameters, binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Entry), binding =>
            binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Parameter.Name == "choose" ? subject.Factory.CreateBooleanValue(choose) : input == null
                ? subject.Factory.CreateNullValue(subject.Factory.GetVariableInfo(binding.Entry).Type)
                : subject.Factory.CreateStringValue(input)));
        var execution = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program, inputs);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }
}
