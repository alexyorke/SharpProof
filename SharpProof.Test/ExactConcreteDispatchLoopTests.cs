using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ExactConcreteDispatchLoopTests
{
    [Test]
    public async Task VirtualMethodDispatch_DoWhileFalseAssignedExactConcreteLocal_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public abstract class Worker
{
    [EnforcePure]
    public abstract int Compute(int value);
}

public class ExactWorker : Worker
{
    [EnforcePure]
    public override int Compute(int value) => value + 1;
}

public class ImpureWorker : ExactWorker
{
    [EnforcePure]
    public override int {|SP0002:Compute|}(int value)
    {
        Console.WriteLine(value);
        return value + 2;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        Worker worker;
        do
        {
            worker = new ExactWorker();
        } while (false);

        return worker.Compute(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task VirtualPropertyDispatch_DoWhileFalseAssignedExactConcreteLocal_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public abstract class BaseValue
{
    public abstract int Value { get; }
}

public class ExactValue : BaseValue
{
    public override int Value => 1;
}

public class ImpureValue : ExactValue
{
    public override int {|SP0002:Value|}
    {
        [EnforcePure]
        get
        {
            Console.WriteLine(1);
            return 2;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int ReadValue()
    {
        BaseValue value;
        do
        {
            value = new ExactValue();
        } while (false);

        return value.Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}