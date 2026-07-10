using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ImmutableQueueTests
{
    [Test]
    public async Task ImmutableQueueEnqueue_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableQueue<int> EnqueueValue(ImmutableQueue<int> queue, int value)
    {
        return queue.Enqueue(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableQueueClear_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableQueue<int> ClearQueue(ImmutableQueue<int> queue)
    {
        return queue.Clear();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableQueueDequeue_Diagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableQueue<int> DequeueValue(ImmutableQueue<int> queue)
    {
        return queue.Dequeue();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                .WithSpan(8, 32, 8, 44)
                .WithArguments("DequeueValue"));
    }

    [Test]
    public async Task IImmutableQueueDequeue_KnownConcreteReceiver_Diagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IImmutableQueue<int> DequeueValue()
    {
        IImmutableQueue<int> queue = ImmutableQueue<int>.Empty;
        return queue.Dequeue();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                .WithSpan(8, 33, 8, 45)
                .WithArguments("DequeueValue"));
    }
}