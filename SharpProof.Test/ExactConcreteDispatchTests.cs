using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ExactConcreteDispatchTests
    {
        [Test]
        public async Task InterfaceDispatch_ExactConcreteLocalWithImpureSubclass_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public interface IWorker
{
    [EnforcePure]
    int Compute(int value);
}

public class ExactWorker : IWorker
{
    [EnforcePure]
    public virtual int Compute(int value) => value + 1;
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

public class WorkerHost
{
    [EnforcePure]
    public int Process(int value)
    {
        IWorker worker = new ExactWorker();
        return worker.Compute(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task VirtualDispatch_ExactConcreteLocalWithImpureSubclass_NoDiagnostic()
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

public class WorkerHost
{
    [EnforcePure]
    public int Process(int value)
    {
        Worker worker = new ExactWorker();
        return worker.Compute(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
