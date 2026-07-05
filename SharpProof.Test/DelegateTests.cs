using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class DelegateTests
    {
        [Test]
        public async Task DelegateWithImpureTarget_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

    public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Action action = () => Console.WriteLine(""Hello"");
        action();
    }
}";


            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                   .WithSpan(8, 17, 8, 27)
                                   .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task ImpureMethodWithDelegate_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Action action = () => Console.WriteLine(""Hello"");
        action();
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                   .WithSpan(8, 17, 8, 27)
                                   .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task PureMethodWithDelegateInvocation_NoDiagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public delegate void MyAction();

public class TestClass
{
    private readonly MyAction _pureAction = () => { var x = 1; }; // Pure target

    [EnforcePure]
    public void TestMethod()
    {
        _pureAction();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task MutableDelegateFieldInitializer_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private Action _callback = PureTarget;

    [EnforcePure]
    public static void PureTarget()
    {
    }

    public static void ImpureTarget()
    {
        Console.WriteLine();
    }

    public void MakeImpure()
    {
        _callback = ImpureTarget;
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        _callback();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task EscapingDelegateCapturesFreshArrayAlias_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Func<int> {|SP0002:TestMethod|}()
    {
        var array = new int[1];
        var alias = array;
        return () => alias[0];
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task ReadonlyDelegateFieldInitializerOverwrittenInConstructor_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private readonly Action _callback = PureTarget;

    public TestClass()
    {
        _callback = ImpureTarget;
    }

    [EnforcePure]
    public static void PureTarget()
    {
    }

    public static void ImpureTarget()
    {
        Console.WriteLine();
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        _callback();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task DelegateInvocationWithImpureArgument_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static int PureTarget(int value)
    {
        return value;
    }

    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        Func<int, int> projector = PureTarget;
        return projector(Console.Read());
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task DelegateReassignedToUnknownTarget_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static void PureTarget()
    {
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}(Action unknown)
    {
        Action action = PureTarget;
        action = unknown;
        action();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task DelegateReassignedToUnknownTargetOnOneBranch_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static void PureTarget()
    {
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}(bool flag, Action unknown)
    {
        Action action = PureTarget;
        if (flag)
        {
            action = unknown;
        }

        action();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task DelegateReassignedByRefCall_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static void PureTarget()
    {
    }

    public static void ImpureTarget()
    {
        Console.WriteLine();
    }

    [PureExternal]
    private static void Replace(ref Action action)
    {
        action = ImpureTarget;
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Action action = PureTarget;
        Replace(ref action);
        action();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task DelegateReassignedThroughRefLocalAlias_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static void PureTarget()
    {
    }

    public static void ImpureTarget()
    {
        Console.WriteLine();
    }

    [PureExternal]
    private static void Replace(ref Action action)
    {
        action = ImpureTarget;
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Action action = PureTarget;
        ref Action alias = ref action;
        Replace(ref alias);
        action();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task DelegateAssignedThroughRefLocalAlias_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static void PureTarget()
    {
    }

    public static void ImpureTarget()
    {
        Console.WriteLine();
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Action action = PureTarget;
        ref Action alias = ref action;
        alias = ImpureTarget;
        action();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task DelegateReassignedAcrossPureTargetsOnBranches_NoDiagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static int PureTarget1()
    {
        return 1;
    }

    [EnforcePure]
    public static int PureTarget2()
    {
        return 2;
    }

    [EnforcePure]
    public int TestMethod(bool flag)
    {
        Func<int> action = PureTarget1;
        if (flag)
        {
            action = PureTarget2;
        }

        return action();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task DelegateAssignedDistinctPureTargetsPerBranch_NoDiagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static int PureTarget1()
    {
        return 1;
    }

    [EnforcePure]
    public static int PureTarget2()
    {
        return 2;
    }

    [EnforcePure]
    public int TestMethod(bool flag)
    {
        Func<int> action;
        if (flag)
        {
            action = PureTarget1;
        }
        else
        {
            action = PureTarget2;
        }

        return action();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task LambdaCapturingFreshArrayAndEscaping_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Func<int> {|SP0002:TestMethod|}()
    {
        var values = new int[1];
        return () => values[0];
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task LocalFunctionDelegateCapturingFreshArrayAndEscaping_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Func<int> {|SP0002:TestMethod|}()
    {
        var values = new int[1];
        return Read;

        int Read()
        {
            return values[0];
        }
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task LambdaCapturingFreshMutableObjectAndEscaping_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public sealed class Counter
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public Func<int> {|SP0002:TestMethod|}()
    {
        var counter = new Counter();
        return () => counter.Value;
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task LambdaCapturingFreshMutableObjectAliasAndEscaping_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public sealed class Counter
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public Func<int> {|SP0002:TestMethod|}()
    {
        var counter = new Counter();
        var alias = counter;
        return () => alias.Value;
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task DelegateInvocation_ConstantConditionalDeadImpureTarget_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static void PureTarget()
    {
    }

    public static void ImpureTarget()
    {
        Console.WriteLine();
    }

    [EnforcePure]
    public void TestMethod()
    {
        Action action = true ? new Action(PureTarget) : new Action(ImpureTarget);
        action();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DelegateCombineWithImpureTarget_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static void PureTarget()
    {
    }

    public static void ImpureTarget()
    {
        Console.WriteLine();
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Action first = PureTarget;
        Action second = ImpureTarget;
        var combined = (Action)Delegate.Combine(first, second);
        combined();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DelegateCombineWithPureTargets_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static void FirstTarget()
    {
    }

    [EnforcePure]
    public static void SecondTarget()
    {
    }

    [EnforcePure]
    public void TestMethod()
    {
        Action first = FirstTarget;
        Action second = SecondTarget;
        _ = Delegate.Combine(first, second);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DelegateMethodGroupFromVirtualReceiver_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class BaseWorker
{
    public virtual void Work()
    {
    }
}

public class ImpureWorker : BaseWorker
{
    public override void Work()
    {
        Console.WriteLine();
    }
}

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(BaseWorker worker)
    {
        Action action = worker.Work;
        action();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitDelegateCreationFromVirtualReceiverReturned_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class BaseWorker
{
    public virtual void Work()
    {
    }
}

public class ImpureWorker : BaseWorker
{
    public override void Work()
    {
        Console.WriteLine();
    }
}

public class TestClass
{
    [EnforcePure]
    public Action {|SP0002:Create|}(BaseWorker worker)
    {
        return new Action(worker.Work);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExplicitDelegateCreationFromFreshVirtualReceiver_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class PureWorker
{
    public virtual void Work()
    {
    }
}

public class TestClass
{
    [EnforcePure]
    public Action Create()
    {
        return new Action(new PureWorker().Work);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DelegateCompoundAddPreservesUnknownTarget_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static void PureTarget()
    {
    }

    [EnforcePure]
    public void {|SP0002:TestMethod|}(Action unknown)
    {
        Action action = unknown;
        action += PureTarget;
        action();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task DelegateInitializedFromPreviousDeclarator_NoDiagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static void PureTarget()
    {
    }

    [EnforcePure]
    public void TestMethod()
    {
        Action first = PureTarget, second = first;
        second();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }

        [Test]
        public async Task DelegateCreationWithImpureReceiver_Diagnostic()
        {
            var testCode = @"
using System;
using SharpProof.Attributes;

public sealed class Receiver
{
    [EnforcePure]
    public Receiver()
    {
    }

    [EnforcePure]
    public void Target()
    {
    }
}

public class TestClass
{
    [EnforcePure]
    private static Receiver Create(int value)
    {
        return new Receiver();
    }

    [EnforcePure]
    public Action {|SP0002:TestMethod|}()
    {
        return Create(Console.Read()).Target;
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(testCode);
        }
    }
}


