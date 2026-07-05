using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ExactConcreteDispatchSwitchStatementTests
    {
        [Test]
        public async Task VirtualMethodDispatch_SameConcreteSwitchStatementMerge_NoDiagnostic()
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
    public int Process(int selector, int value)
    {
        Worker worker;
        switch (selector)
        {
            case 0:
                worker = new ExactWorker();
                break;
            case 1:
                worker = new ExactWorker();
                break;
            default:
                worker = new ExactWorker();
                break;
        }

        return worker.Compute(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task VirtualPropertyDispatch_SameConcreteSwitchStatementMerge_NoDiagnostic()
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
    public int ReadValue(int selector)
    {
        BaseValue value;
        switch (selector)
        {
            case 0:
                value = new ExactValue();
                break;
            case 1:
                value = new ExactValue();
                break;
            default:
                value = new ExactValue();
                break;
        }

        return value.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
