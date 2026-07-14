using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class EventTests
{
    [Test]
    public async Task EventSnapshotRead_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    // Declaring an event is pure
    public event EventHandler TestEvent;

    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        // Reading the event backing delegate observes mutable subscriber state.
        var evt = TestEvent;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImpureMethodWithEvent_Diagnostic()
    {
        var testCode = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    public event Action MyEvent;

    [EnforcePure]
    public void TestMethod()
    {
        // Event subscription modifies state and should be impure.
        MyEvent += () => Console.WriteLine(); // Event assignment is reported as mutable state write.
    }
}
";


        var expectedDiagnostic = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(10, 17, 10, 27)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(testCode, expectedDiagnostic);
    }

    [Test]
    public async Task MethodWithEventSubscription_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class EventSource
{
    public event EventHandler TestEvent;
    // Base helper is intentionally unannotated so this test only expects diagnostics on enforced methods.
    protected virtual void OnTestEvent(object sender, EventArgs e) => TestEvent?.Invoke(this, e); // Added parameters
}

public class TestClass : EventSource
{
    [EnforcePure] // Impure: Event subscription modifies state
    public void TestMethod()
    {
        this.TestEvent += OnTestEvent;
    }

    [EnforcePure] // Impure: Console.WriteLine
    protected override void OnTestEvent(object sender, EventArgs e) // Added parameters
    {
        Console.WriteLine(""Event handled"");
    }
}";


        var expectedTestMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(15, 17, 15, 27)
            .WithArguments("TestMethod");
        var expectedOnTestEventOverride = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(21, 29, 21, 40).WithArguments("OnTestEvent");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedTestMethod, expectedOnTestEventOverride);
    }
}