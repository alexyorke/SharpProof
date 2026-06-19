using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;
using PurelySharp.Attributes;
using System;

namespace PurelySharp.Test
{
    [TestFixture]
    public class InterfaceInteractionTests
    {
        [Test]
        public async Task PureInterfaceImplementation_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public interface ICalculator
{
    [EnforcePure] int Add(int a, int b);
    [EnforcePure] int Multiply(int a, int b);
}

public class SimpleCalculator : ICalculator
{
    public int Add(int a, int b) => a + b;

    public int Multiply(int a, int b) => a * b;
}

public class Usage
{
    [EnforcePure]
    public int UseCalculator(ICalculator calc, int x, int y)
    {
        return calc.Multiply(calc.Add(x, y), 2);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImpureInterfaceImplementation_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public interface ILogger
{
    [EnforcePure] void Log(string message);
}

public class ConsoleLogger : ILogger
{
    public void Log(string message)
    {
        Console.WriteLine(message); // Impure call
    }
}

public class Service
{
    private ILogger _logger;
    public Service(ILogger logger)
    {
        _logger = logger;
    }

    [EnforcePure]
    public void DoWork(string data)
    {
        // This call becomes impure because the underlying Log is impure
        _logger.Log($""Processing: {data}"");
    }
}
";

            var expectedLog = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002)
                .WithSpan(12, 17, 12, 20)
                .WithArguments("Log");
            var expectedCtor = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004)
                .WithSpan(21, 12, 21, 19)
                .WithArguments(".ctor");
            var expectedDoWork = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002)
                .WithSpan(27, 17, 27, 23)
                .WithArguments("DoWork");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedLog, expectedCtor, expectedDoWork);
        }

        [Test]
        public async Task InternalInterfaceWithoutKnownImplementation_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

internal interface IWorker
{
    int Compute(int value);
}

public class WorkerHost
{
    [EnforcePure]
    internal int {|PS0002:ComputeWithUnknownImplementation|}(IWorker worker, int value)
    {
        return worker.Compute(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PublicInterfaceWithoutKnownImplementation_ConservativeImpure()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IPublicWorker
{
    int Compute(int value);
}

public class WorkerHost
{
    [EnforcePure]
    public int {|PS0002:ComputeWithUnknownPublicImplementation|}(IPublicWorker worker, int value)
    {
        return worker.Compute(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DefaultInterfaceImplementation_Pure_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

internal interface ICounter
{
    int Increment(int value) => value + 1;
}

public class TestClass
{
    [EnforcePure]
    internal int Process(ICounter counter, int value)
    {
        return counter.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PublicDefaultInterfaceImplementation_ConservativeImpure()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IPublicCounter
{
    int Increment(int value) => value + 1;
}

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:Process|}(IPublicCounter counter, int value)
    {
        return counter.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DefaultInterfaceImplementation_SealedImplementation_ConsidersDefaultMethod()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public interface ICounter
{
    int Increment(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public sealed class PureCounter : ICounter
{
}

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:Process|}(PureCounter counter, int value)
    {
        return ((ICounter)counter).Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DefaultInterfaceImplementation_AllocationToSealedCast_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface ICounter
{
    int Increment(int value) => value + 1;
}

public sealed class SealedCounter : ICounter
{
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        return ((ICounter)new SealedCounter()).Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DefaultInterfaceImplementation_StructAllocationCast_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface ICounter
{
    int Increment(int value) => value + 1;
}

public readonly struct StructCounter : ICounter
{
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        return ((ICounter)new StructCounter()).Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitInterfaceImplementation_Pure_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IExplicitCounter
{
    int Increment(int value);
}

public class ExplicitCounter : IExplicitCounter
{
    int IExplicitCounter.Increment(int value)
    {
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(IExplicitCounter counter, int value)
    {
        return counter.Increment(value);
    }
        }
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitInterfaceImplementation_ThroughCast_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IExplicitCounter
{
    int Increment(int value);
}

public sealed class ExplicitCounter : IExplicitCounter
{
    int IExplicitCounter.Increment(int value)
    {
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        return ((IExplicitCounter)new ExplicitCounter()).Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitInterfaceImplementation_ThroughAsCast_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IExplicitCounter
{
    int Increment(int value);
}

public sealed class AsExplicitCounter : IExplicitCounter
{
    int IExplicitCounter.Increment(int value)
    {
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        return (new AsExplicitCounter() as IExplicitCounter)!.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StructInterfaceImplementation_Pure_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IStructCounter
{
    int Increment(int value);
}

public struct StructCounter : IStructCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(IStructCounter counter, int value)
    {
        return counter.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InternalInterface_MultiplePureImplementations_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

internal interface ICounter
{
    int Increment(int value);
}

internal class FastCounter : ICounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

internal class SlowCounter : ICounter
{
    public int Increment(int value)
    {
        return value + 2;
    }
}

public class TestClass
{
    [EnforcePure]
    internal int Process(ICounter counter, int value)
    {
        return counter.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task GenericInterfaceConstraint_ConservativeImpure()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IMixedCounter
{
    int Increment(int value);
}

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:ProcessGeneric|}<T>(T counter, int value) where T : IMixedCounter
    {
        return counter.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task GenericInterfaceConstraint_WithKnownPureImplementation_StillConservativeImpure()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IPublicWorker
{
    int Compute(int value);
}

public sealed class PureWorker : IPublicWorker
{
    public int Compute(int value)
    {
        return value + 1;
    }
}

public class WorkerHost
{
    [EnforcePure]
    public int {|PS0002:Run|}<T>(T worker, int value) where T : IPublicWorker
    {
        return worker.Compute(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfaceImplementation_MixedPurity_ConservativeImpure()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public interface IMixedCounter
{
    int Increment(int value) => value + 1;
}

public class BadCounter : IMixedCounter
{
    public int Increment(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:ProcessMixedDispatch|}(IMixedCounter counter, int value)
    {
        return counter.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BaseReference_UsesBaseDispatchOnly()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public abstract class BaseWorker
{
    public virtual int Compute(int value)
    {
        return value;
    }
}

public class ImpureOverride : BaseWorker
{
    public override int Compute(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public class PureWorkerHost : BaseWorker
{
    [EnforcePure]
    public int ComputeWithBaseCall(int value)
    {
        return base.Compute(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BaseReferenceInOverride_UsesBaseTargetOnly()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public abstract class BaseWorker
{
    public virtual int Compute(int value)
    {
        return value;
    }
}

public class PureHost : BaseWorker
{
    public override int Compute(int value)
    {
        return base.Compute(value);
    }

    [EnforcePure]
    public int PureBaseCall(int value)
    {
        return base.Compute(value);
    }
}

public class BadWorker : BaseWorker
{
    public override int Compute(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TransitiveVirtualOverride_ConservativeImpure()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public abstract class BaseWorker
{
    public virtual int Compute(int value)
    {
        return value;
    }
}

public class MidWorker : BaseWorker
{
    public override int Compute(int value)
    {
        return value + 1;
    }
}

public class BadWorker : MidWorker
{
    public override int Compute(int value)
    {
        Console.WriteLine(value);
        return value + 2;
    }
}

public class WorkerHost
{
    [EnforcePure]
    public int {|PS0002:ComputeWithVirtualDispatch|}(BaseWorker worker, int value)
    {
        return worker.Compute(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PublicVirtualDispatch_ConservativeImpure()
        {
            var test = @"
using PurelySharp.Attributes;

public class BaseWorker
{
    [EnforcePure]
    public virtual int Compute(int value) => value;
}

public class WorkerHost
{
    [EnforcePure]
    public int {|PS0002:ComputeWithVirtualDispatch|}(BaseWorker worker, int value)
    {
        return worker.Compute(value);
    }
        }
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PublicVirtualDispatch_OnSealedImplementationThroughBaseCast_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public class BaseWorker
{
    public virtual int Compute(int value) => value;
}

public sealed class PureWorker : BaseWorker
{
    public override int Compute(int value) => value + 1;
}

public sealed class ImpureWorker : BaseWorker
{
    public override int Compute(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public class WorkerHost
{
    [EnforcePure]
    public int ComputeWithVirtualDispatch(int value)
    {
        return ((BaseWorker)new PureWorker()).Compute(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InternalVirtualMethod_NoExternalOverrides_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class BaseWorker
{
    internal virtual int Compute(int value)
    {
        return value;
    }
}

internal class InternalWorker : BaseWorker
{
    internal override int Compute(int value)
    {
        return value + 1;
    }
}

public class WorkerHost
{
    [EnforcePure]
    public int ComputeWithInternalVirtualDispatch(BaseWorker worker, int value)
    {
        return worker.Compute(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PrivateProtectedVirtualMethod_NoExternalOverrides_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class BaseWorker
{
    private protected virtual int Compute(int value)
    {
        return value;
    }

    [EnforcePure]
    public int ComputeFromBase(int value)
    {
        return Compute(value);
    }
}

internal class InternalWorker : BaseWorker
{
    private protected override int Compute(int value)
    {
        return value + 1;
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ProtectedOrInternalVirtualDispatch_ConservativeImpure()
        {
            var test = @"
using PurelySharp.Attributes;

public class BaseWorker
{
    protected internal virtual int Compute(int value)
    {
        return value;
    }
}

public class WorkerHost
{
    [EnforcePure]
    public int {|PS0002:ComputeWithProtectedOrInternalVirtual|}(BaseWorker worker, int value)
    {
        return worker.Compute(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ProtectedOrInternalVirtualDispatch_OnSealedReceiver_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class BaseWorker
{
    protected internal virtual int Compute(int value)
    {
        return value;
    }
}

public sealed class DerivedWorker : BaseWorker
{
    protected internal override int Compute(int value)
    {
        return value + 1;
    }
}

public class WorkerHost
{
    [EnforcePure]
    public int ComputeWithProtectedOrInternalVirtual(DerivedWorker worker, int value)
    {
        return worker.Compute(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task NestedInterfaceInInternalContainer_WithInternalDefaultImplementation_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

internal class HostContainer
{
    public interface INestedCounter
    {
        int Increment(int value) => value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    internal int Process(HostContainer.INestedCounter counter, int value)
    {
        return counter.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PublicSealedClass_NonVirtualCall_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public sealed class SealedWorker
{
    [EnforcePure]
    public int Compute(int value)
    {
        return value + 1;
    }
}

public class WorkerHost
{
    [EnforcePure]
    public int ComputeWithSealedVirtual(SealedWorker worker, int value)
    {
        return worker.Compute(value);
    }
        }
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfaceMethod_OnSealedImplementation_ThroughCast_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface ICastCounter
{
    int Increment(int value);
}

public sealed class SealedCastCounter : ICastCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        return ((ICastCounter)new SealedCastCounter()).Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfaceMethod_OnSealedImplementation_ThroughCast_WithMixedCandidates_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public interface ICastCounter
{
    int Increment(int value);
}

public sealed class SealedCastCounter : ICastCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class BadCastCounter : ICastCounter
{
    public int Increment(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        return ((ICastCounter)new SealedCastCounter()).Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfaceMethod_OnLocalInitializedWithSealedImplementation_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public interface ILocalCounter
{
    int Increment(int value);
}

public sealed class SealedLocalCounter : ILocalCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class ImpureLocalCounter : ILocalCounter
{
    public int Increment(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        ILocalCounter counter = new SealedLocalCounter();
        return counter.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfaceMethod_OnLocalInitializedFromPreviousDeclarator_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public interface IAliasCounter
{
    int Increment(int value);
}

public sealed class SealedAliasCounter : IAliasCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class ImpureAliasCounter : IAliasCounter
{
    public int Increment(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        IAliasCounter first = new SealedAliasCounter(), second = first;
        return second.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfaceMethod_OnLocalReassignedFromUnknownImplementation_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public interface IReassignedCounter
{
    int Increment(int value);
}

public sealed class SealedReassignedCounter : IReassignedCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class ImpureReassignedCounter : IReassignedCounter
{
    public int Increment(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:Process|}(IReassignedCounter unknown, int value)
    {
        IReassignedCounter counter = new SealedReassignedCounter();
        counter = unknown;
        return counter.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfaceMethod_OnLocalReassignedByRefCall_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public interface IRefCounter
{
    int Increment(int value);
}

public sealed class SealedRefCounter : IRefCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class ImpureRefCounter : IRefCounter
{
    public int Increment(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public class TestClass
{
    [PureExternal]
    private static void Replace(ref IRefCounter counter)
    {
        counter = new ImpureRefCounter();
    }

    [EnforcePure]
    public int {|PS0002:Process|}(int value)
    {
        IRefCounter counter = new SealedRefCounter();
        Replace(ref counter);
        return counter.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfaceMethod_OnSealedImplementation_ThroughAsCast_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IAsCounter
{
    int Increment(int value);
}

public sealed class AsCastCounter : IAsCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(AsCastCounter counter, int value)
    {
        return (counter as IAsCounter)!.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfaceMethod_OnAllocationCast_ThroughConditionalAccess_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface ICastCounter
{
    int Increment(int value);
}

public sealed class SealedCastCounter : ICastCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        return (new SealedCastCounter() as ICastCounter)?.Increment(value) ?? 0;
    }
        }
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StructInterfaceMethod_OnAllocationCast_ThroughAsConditionalAccess_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IStructCounter
{
    int Increment(int value);
}

public struct StructCounter : IStructCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        return (new StructCounter() as IStructCounter)?.Increment(value) ?? 0;
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterfaceMethod_OnConditionalReceiverBranchesWithSameSealedImplementation_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public interface IConditionalCounter
{
    int Increment(int value);
}

public sealed class SealedConditionalCounter : IConditionalCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class ImpureConditionalCounter : IConditionalCounter
{
    public int Increment(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(bool useLeft, int value)
    {
        return (useLeft
            ? (IConditionalCounter)new SealedConditionalCounter()
            : (IConditionalCounter)new SealedConditionalCounter()).Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InternalInterfaceBaseCast_ToPublicInterface_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public interface IPublicCounter
{
    int Increment(int value);
}

internal interface IInternalCounter : IPublicCounter
{
    int Increment(int value) => value + 1;
}

public class TestClass
{
    [EnforcePure]
    internal int Process(IInternalCounter counter, int value)
    {
        return (counter as IPublicCounter)!.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PublicBaseInterfaceCast_FromDerivedInterface_KeepsDispatchNarrowed_NoConservativeDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;

public interface IBaseCounter
{
    int Increment(int value);
}

public interface IDerivedCounter : IBaseCounter
{
    int Increment(int value);
}

public class PureDerivedCounter : IDerivedCounter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class ImpureBaseCounter : IBaseCounter
{
    public int Increment(int value)
    {
        Console.WriteLine(value);
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(IDerivedCounter counter, int value)
    {
        return (counter as IBaseCounter)!.Increment(value);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
