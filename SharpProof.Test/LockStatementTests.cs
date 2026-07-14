using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class LockStatementTests
{
    [Test]
    public async Task LockStatement_ImpureByDefault()
    {
        var test = @"
using System;
using SharpProof.Attributes;


[AttributeUsage(AttributeTargets.Method)]
public class AllowSynchronizationAttribute : Attribute { }

public class TestClass
{
    private readonly object _lock = new object();

    [EnforcePure]
    public void ImpureMethod()
    {
        lock (_lock)
        {
            Console.WriteLine(""Inside lock"");
        }
    }
}";


        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(14, 17, 14, 29)
            .WithArguments("ImpureMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }


    [Test]
    public async Task AllowSynchronization_WithoutPurityAttribute_Warns()
    {
        var test = @"
using System;
using SharpProof.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class AllowSynchronizationAttribute : Attribute { }

public class C
{
    [AllowSynchronization]
    public void M() { }
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeId)
            .WithSpan(11, 17, 11, 18)
            .WithArguments("M");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }


    [Test]
    public async Task LockStatement_WithPureOperations_RemainsConservativelyImpure()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Diagnostics;



[AttributeUsage(AttributeTargets.Method)]
public class AllowSynchronizationAttribute : Attribute { }

public class TestClass
{
    private readonly object _lock = new object();
    private readonly int _value = 42;

    [EnforcePure]
    [AllowSynchronization]
    public int PureMethodWithLock()
    {
        int result;
        lock (_lock)
        {
            result = _value;
        }
        return result;
    }
}";


        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(18, 16, 18, 34)
            .WithArguments("PureMethodWithLock");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }


    [Test]
    public async Task LockStatement_WithPureReads_RemainsConservativelyImpure()
    {
        var test = @"
using System;
using SharpProof.Attributes;


[AttributeUsage(AttributeTargets.Method)]
public class AllowSynchronizationAttribute : Attribute { }

class Program
{
    private readonly object _lock = new object();
    private readonly int[] _array = new int[10];

    [EnforcePure]
    [AllowSynchronization]
    public int PureMethodWithLock()
    {
        lock (_lock)
        {
            return _array[0]; // Pure operation - just reading
        }
    }
}";


        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(16, 16, 16, 34)
            .WithArguments("PureMethodWithLock");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }


    [Test]
    public async Task LockStatement_WithImpureOperations_IsImpure()
    {
        var test = @"
using System;
using SharpProof.Attributes;


[AttributeUsage(AttributeTargets.Method)]
public class AllowSynchronizationAttribute : Attribute { }

class Program
{
    private readonly object _lock = new object();
    private int _value = 0;

    [EnforcePure]
    [AllowSynchronization]
    public void ImpureMethodWithLock()
    {
        lock (_lock)
        {
            _value++; // This is impure because it modifies state
        }
    }
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(16, 17, 16, 37)
            .WithArguments("ImpureMethodWithLock");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }


    [Test]
    public async Task LockStatement_NonReadonlyObject_IsImpure()
    {
        var test = @"
using System;
using SharpProof.Attributes;


[AttributeUsage(AttributeTargets.Method)]
public class AllowSynchronizationAttribute : Attribute { }

class Program
{
    private object _nonReadonlyLock = new object(); // Non-readonly lock object
    private int _counter = 0;

    [EnforcePure]
    [AllowSynchronization]
    public void ImpureMethodWithNonReadonlyLock()
    {
        lock (_nonReadonlyLock)
        {
            _counter++; // This is the impure operation
        }
    }
}";


        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(16, 17, 16, 48)
            .WithArguments("ImpureMethodWithNonReadonlyLock");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task AllowSynchronization_WithPurityAttribute_ButNoLock_WarnsSP0008()
    {
        var test = @"
using System;
using SharpProof.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class AllowSynchronizationAttribute : Attribute { }

public class C
{
    [EnforcePure]
    [AllowSynchronization]
    public int M() => 42;
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.RedundantAllowSynchronizationId)
            .WithSpan(12, 16, 12, 17)
            .WithArguments("M");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task AllowSynchronization_WithLock_DoesNotWarnSP0008()
    {
        var test = @"
using System;
using SharpProof.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class AllowSynchronizationAttribute : Attribute { }

public class C
{
    private readonly object _gate = new object();

    [EnforcePure]
    [AllowSynchronization]
    public int M()
    {
        lock (_gate) { return 1; }
    }
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(14, 16, 14, 17)
            .WithArguments("M");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}